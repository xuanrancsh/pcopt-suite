// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;

namespace PcOpt.Core.Game;

/// <summary>库扫描报告（T7）。</summary>
/// <param name="Games">全部已识别游戏（去重后）。</param>
/// <param name="PlatformErrors">各平台失败原因（平台 → 错误）；成功平台不出现在此。</param>
public sealed record LibraryScanReport(
    IReadOnlyList<InstalledGame> Games,
    IReadOnlyDictionary<GamePlatform, string> PlatformErrors);

/// <summary>
/// 游戏库扫描器（架构 3.4：六大平台）。
/// 策略：
/// <list type="bullet">
/// <item>Steam / Epic：平台清单文件确认（Confirmed）；</item>
/// <item>GOG / Xbox / WeGame：卸载注册表保守探测（Inferred），探测不到不归入——由 UI 引导"手动添加"，
/// 绝不猜路径硬编码（待明确事项 #3：WeGame 目录结构真机实测后回填）；</item>
/// <item>单平台失败不影响其他平台（部分结果可用）；全部 IO 只读，不碰游戏文件（合规红线 #3）。</item>
/// </list>
/// </summary>
public sealed class GameLibraryScanner
{
    private readonly IGameSourcePaths _paths;
    private readonly IUninstallRegistryReader _uninstallReader;
    private readonly ILogger<GameLibraryScanner> _logger;

    /// <summary>初始化扫描器。</summary>
    public GameLibraryScanner(
        IGameSourcePaths paths,
        IUninstallRegistryReader uninstallReader,
        ILogger<GameLibraryScanner> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _uninstallReader = uninstallReader ?? throw new ArgumentNullException(nameof(uninstallReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>扫描全部平台。</summary>
    public async Task<Result<LibraryScanReport>> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(() => ScanInternal(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result<LibraryScanReport>.Fail("游戏库扫描已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "游戏库扫描失败");
            return Result<LibraryScanReport>.Fail($"游戏库扫描失败：{ex.Message}");
        }
    }

    /// <summary>扫描 Steam 库（libraryfolders.vdf + appmanifest_*.acf）。</summary>
    public Result<IReadOnlyList<InstalledGame>> ScanSteamLibraries()
    {
        var libraryRoots = CollectSteamLibraryRoots();
        if (libraryRoots.Count == 0)
        {
            return Result<IReadOnlyList<InstalledGame>>.Ok(Array.Empty<InstalledGame>());
        }

        var games = new List<InstalledGame>();
        var seenAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in libraryRoots)
        {
            var steamApps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            string[] manifestFiles;
            try
            {
                manifestFiles = Directory.GetFiles(steamApps, "appmanifest_*.acf");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "读取 Steam 库目录失败：{Root}", root);
                continue;
            }

            foreach (var manifestFile in manifestFiles)
            {
                var game = ParseSteamManifest(manifestFile, root);
                if (game is null || !seenAppIds.Add(game.GameId))
                {
                    continue;
                }

                games.Add(game);
            }
        }

        return Result<IReadOnlyList<InstalledGame>>.Ok(games);
    }

    /// <summary>扫描 Epic 库（%ProgramData%\...\Manifests\*.item）。</summary>
    public Result<IReadOnlyList<InstalledGame>> ScanEpicLibrary()
    {
        var manifestsDirectory = _paths.EpicManifestsDirectory;
        if (manifestsDirectory is null || !Directory.Exists(manifestsDirectory))
        {
            return Result<IReadOnlyList<InstalledGame>>.Ok(Array.Empty<InstalledGame>());
        }

        string[] itemFiles;
        try
        {
            itemFiles = Directory.GetFiles(manifestsDirectory, "*.item");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<IReadOnlyList<InstalledGame>>.Fail($"读取 Epic 清单目录失败：{ex.Message}");
        }

        var games = new List<InstalledGame>();
        foreach (var itemFile in itemFiles)
        {
            string json;
            try
            {
                json = File.ReadAllText(itemFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(ex, "读取 Epic 清单失败：{File}", Path.GetFileName(itemFile));
                continue;
            }

            var parsed = EpicManifestReader.Parse(json);
            if (!parsed.IsSuccess || parsed.Value is not { } item)
            {
                _logger.LogDebug("跳过无效 Epic 清单 {File}：{Error}", Path.GetFileName(itemFile), parsed.Error);
                continue;
            }

            var installPath = NormalizeExistingDirectory(item.InstallLocation);
            games.Add(new InstalledGame(
                item.AppName,
                item.DisplayName,
                GamePlatform.Epic,
                installPath,
                Array.Empty<string>(),
                item.LaunchExecutable,
                GameDetectionConfidence.Confirmed));
        }

        return Result<IReadOnlyList<InstalledGame>>.Ok(games);
    }

    /// <summary>
    /// 扫描注册表类平台（GOG / Xbox / WeGame 保守探测）。
    /// 探测不到的条目不归入任何平台（由 UI 引导手动添加，不猜测）。
    /// </summary>
    public Result<IReadOnlyList<InstalledGame>> ScanRegistryPlatforms()
    {
        IReadOnlyList<UninstallEntry> entries;
        try
        {
            entries = _uninstallReader.ReadEntries();
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<InstalledGame>>.Fail($"读取卸载注册表失败：{ex.Message}");
        }

        var games = new List<InstalledGame>();
        foreach (var entry in entries)
        {
            var platform = ClassifyByRegistryMarkers(entry);
            if (platform is null)
            {
                continue;
            }

            var displayName = entry.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            games.Add(new InstalledGame(
                entry.KeyName,
                displayName.Trim(),
                platform.Value,
                NormalizeExistingDirectory(entry.InstallLocation),
                Array.Empty<string>(),
                null,
                GameDetectionConfidence.Inferred));
        }

        return Result<IReadOnlyList<InstalledGame>>.Ok(games);
    }

    private Result<LibraryScanReport> ScanInternal(CancellationToken cancellationToken)
    {
        var games = new List<InstalledGame>();
        var errors = new Dictionary<GamePlatform, string>();

        ScanPlatform(GamePlatform.Steam, ScanSteamLibraries, games, errors, cancellationToken);
        ScanPlatform(GamePlatform.Epic, ScanEpicLibrary, games, errors, cancellationToken);
        ScanPlatform(GamePlatform.Unknown, ScanRegistryPlatforms, games, errors, cancellationToken);

        // 注册表类平台细分为 GOG / Xbox / WeGame（按平台聚合）
        var platformErrors = new Dictionary<GamePlatform, string>();
        foreach (var (platform, error) in errors)
        {
            platformErrors[platform] = error;
        }

        return Result<LibraryScanReport>.Ok(new LibraryScanReport(games, platformErrors));
    }

    private void ScanPlatform(
        GamePlatform platform,
        Func<Result<IReadOnlyList<InstalledGame>>> scan,
        List<InstalledGame> games,
        Dictionary<GamePlatform, string> errors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = scan();
            if (result.IsSuccess)
            {
                games.AddRange(result.Value!);
                _logger.LogInformation(
                    "{Platform} 平台扫描到 {Count} 款游戏",
                    platform == GamePlatform.Unknown ? "注册表类" : platform.ToString(),
                    result.Value!.Count);
            }
            else
            {
                errors[platform] = result.Error;
                _logger.LogWarning("{Platform} 平台扫描失败：{Error}", platform, result.Error);
            }
        }
        catch (Exception ex)
        {
            errors[platform] = ex.Message;
            _logger.LogWarning(ex, "{Platform} 平台扫描异常", platform);
        }
    }

    /// <summary>收集 Steam 库根目录：安装目录 + libraryfolders.vdf 声明的库 + 额外配置库。</summary>
    private List<string> CollectSteamLibraryRoots()
    {
        var roots = new List<string>();

        void AddRoot(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(path);
            }
        }

        AddRoot(_paths.SteamInstallPath);

        if (!string.IsNullOrWhiteSpace(_paths.SteamInstallPath))
        {
            foreach (var libraryPath in ReadLibraryFoldersVdf(_paths.SteamInstallPath))
            {
                AddRoot(libraryPath);
            }
        }

        foreach (var extra in _paths.ExtraSteamLibraryPaths)
        {
            AddRoot(extra);
        }

        return roots;
    }

    /// <summary>解析 libraryfolders.vdf，提取全部 "path" 值（兼容新旧书写风格）。</summary>
    internal static IReadOnlyList<string> ReadLibraryFoldersVdf(string steamInstallPath)
    {
        var vdfPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            return Array.Empty<string>();
        }

        string text;
        try
        {
            text = File.ReadAllText(vdfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        var document = KeyValuesParser.TryParse(text);
        return document is null ? Array.Empty<string>() : CollectPaths(document);
    }

    /// <summary>深度遍历 KeyValues 树，收集所有名为 "path" 的字符串值。</summary>
    private static IReadOnlyList<string> CollectPaths(IReadOnlyDictionary<string, object> node)
    {
        var paths = new List<string>();
        CollectPathsCore(node, paths);
        return paths;
    }

    private static void CollectPathsCore(IReadOnlyDictionary<string, object> node, List<string> paths)
    {
        foreach (var (key, value) in node)
        {
            if (value is Dictionary<string, object> child)
            {
                CollectPathsCore(child, paths);
            }
            else if (string.Equals(key, "path", StringComparison.OrdinalIgnoreCase) && value is string path
                && !string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }
    }

    /// <summary>解析单个 appmanifest_*.acf。</summary>
    private static InstalledGame? ParseSteamManifest(string manifestFile, string libraryRoot)
    {
        string text;
        try
        {
            text = File.ReadAllText(manifestFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var document = KeyValuesParser.TryParse(text);
        var appState = document is null ? null : KeyValuesParser.GetObject(document, "AppState");
        if (appState is null)
        {
            return null;
        }

        var appId = KeyValuesParser.GetString(appState, "appid");
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        var name = KeyValuesParser.GetString(appState, "name") ?? appId;
        var installDir = KeyValuesParser.GetString(appState, "installdir");
        string? installPath = null;
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            var candidate = Path.Combine(libraryRoot, "steamapps", "common", installDir);
            installPath = Directory.Exists(candidate) ? candidate : null;
        }

        return new InstalledGame(
            appId,
            name,
            GamePlatform.Steam,
            installPath,
            Array.Empty<string>(),
            null,
            GameDetectionConfidence.Confirmed);
    }

    /// <summary>规范化目录：空 / 不存在返回 null（如实反映，不猜测路径）。</summary>
    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim();
        return Directory.Exists(trimmed) ? trimmed : null;
    }

    /// <summary>注册表条目的平台分类（发行商标记保守匹配；不匹配返回 null）。</summary>
    internal static GamePlatform? ClassifyByRegistryMarkers(UninstallEntry entry)
    {
        var publisher = entry.Publisher ?? string.Empty;
        var url = entry.UrlInfoAbout ?? string.Empty;
        var displayName = entry.DisplayName ?? string.Empty;

        if (publisher.Contains("gog.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("gog.com", StringComparison.OrdinalIgnoreCase))
        {
            return GamePlatform.Gog;
        }

        if (publisher.Contains("game pass", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
        {
            return GamePlatform.Xbox;
        }

        if (publisher.Contains("wegame", StringComparison.OrdinalIgnoreCase)
            || publisher.Contains("腾讯游戏", StringComparison.Ordinal)
            || publisher.Contains("tencent games", StringComparison.OrdinalIgnoreCase))
        {
            return GamePlatform.WeGame;
        }

        return null;
    }
}
