// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Hardware;

namespace PcOpt.Core.Game;

/// <summary>已知反作弊产品（数据库标记 + T9 特征库共用）。</summary>
public enum KnownAntiCheat
{
    /// <summary>未使用已知反作弊。</summary>
    None = 0,

    /// <summary>Valve Anti-Cheat（用户态）。</summary>
    Vac,

    /// <summary>Easy Anti-Cheat（Epic Games）。</summary>
    EasyAntiCheat,

    /// <summary>BattlEye。</summary>
    BattlEye,

    /// <summary>Riot Vanguard（内核级）。</summary>
    Vanguard,

    /// <summary>Activision Ricochet（Call of Duty）。</summary>
    Ricochet,

    /// <summary>腾讯 ACE（内核级）。</summary>
    TencentAce,

    /// <summary>FACEIT 反作弊（内核级）。</summary>
    Faceit,

    /// <summary>已知使用反作弊但不在特征库覆盖范围（保守标记，不猜测）。</summary>
    Unknown,
}

/// <summary>游戏数据库条目（架构 3.4：rules/games.yaml）。</summary>
/// <param name="Id">游戏唯一 Id。</param>
/// <param name="Name">中文人话名。</param>
/// <param name="Executables">可执行文件名集合（不含路径，ETW 匹配用）。</param>
/// <param name="Platforms">通常安装平台。</param>
/// <param name="AntiCheat">已知反作弊产品（运行时以 T9 检测为准）。</param>
/// <param name="RecommendedTier">推荐硬件档位。</param>
public sealed record GameInfo(
    string Id,
    string Name,
    IReadOnlyList<string> Executables,
    IReadOnlyList<GamePlatform> Platforms,
    KnownAntiCheat AntiCheat,
    HardwareTier RecommendedTier);

/// <summary>
/// 游戏数据库（架构 3.4：rules/games.yaml，首发 ≥ 60 款，社区可 PR 增补）。
/// 与 ETW 匹配逻辑解耦：<see cref="FindByExecutable"/> 供 T8 IProcessMatcher 消费。
/// </summary>
public sealed class GameDatabase
{
    /// <summary>最低收录数量（架构 3.4 验收线，防止误删条目后仍能加载）。</summary>
    public const int MinimumGameCount = 60;

    private readonly Dictionary<string, GameInfo> _byId;
    private readonly Dictionary<string, GameInfo> _byExecutable;

    private GameDatabase(IReadOnlyList<GameInfo> games)
    {
        Games = games;
        _byId = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        _byExecutable = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            _byId[game.Id] = game;
            foreach (var executable in game.Executables)
            {
                _byExecutable[NormalizeExecutable(executable)] = game;
            }
        }
    }

    /// <summary>全部游戏条目（按 Id 排序，稳定输出）。</summary>
    public IReadOnlyList<GameInfo> Games { get; }

    /// <summary>从 YAML 文本加载数据库。</summary>
    public static Result<GameDatabase> LoadFromYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return Result<GameDatabase>.Fail("游戏数据库内容为空");
        }

        List<object>? rows;
        try
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
            rows = deserializer.Deserialize<List<object>>(new StringReader(yaml));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return Result<GameDatabase>.Fail($"游戏数据库 YAML 解析失败：{ex.Message}");
        }

        if (rows is null || rows.Count == 0)
        {
            return Result<GameDatabase>.Fail("游戏数据库为空（应为条目列表）");
        }

        var games = new List<GameInfo>(rows.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            var game = ParseEntry(rows[i], i);
            if (!game.IsSuccess)
            {
                return Result<GameDatabase>.Fail(game.Error);
            }

            if (!seenIds.Add(game.Value!.Id))
            {
                return Result<GameDatabase>.Fail($"游戏数据库条目 {i} 的 Id 重复：{game.Value!.Id}");
            }

            games.Add(game.Value!);
        }

        return Result<GameDatabase>.Ok(new GameDatabase(games));
    }

    /// <summary>从文件加载数据库。</summary>
    public static Result<GameDatabase> LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<GameDatabase>.Fail("游戏数据库路径为空");
        }

        try
        {
            return LoadFromYaml(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<GameDatabase>.Fail($"读取游戏数据库失败：{ex.Message}");
        }
    }

    /// <summary>按可执行文件名查找（大小写不敏感；可带或不带 .exe；可带路径）。</summary>
    public GameInfo? FindByExecutable(string? executableNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(executableNameOrPath))
        {
            return null;
        }

        return _byExecutable.TryGetValue(NormalizeExecutable(executableNameOrPath), out var game) ? game : null;
    }

    /// <summary>按游戏 Id 查找。</summary>
    public GameInfo? FindById(string? gameId) =>
        gameId is not null && _byId.TryGetValue(gameId, out var game) ? game : null;

    /// <summary>规范化可执行名：取文件名、小写、去 .exe 后缀。</summary>
    internal static string NormalizeExecutable(string executableNameOrPath)
    {
        var fileName = Path.GetFileName(executableNameOrPath.Trim().Replace('/', '\\'));
        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName = fileName[..^4];
        }

        return fileName.ToLowerInvariant();
    }

    private static Result<GameInfo> ParseEntry(object? node, int index)
    {
        if (node is not Dictionary<object, object> map)
        {
            return Result<GameInfo>.Fail($"游戏数据库条目 {index} 格式无效：应为映射");
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in map)
        {
            fields[entry.Key?.ToString() ?? string.Empty] = entry.Value ?? string.Empty;
        }

        var id = GetString(fields, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result<GameInfo>.Fail($"游戏数据库条目 {index} 缺少 id");
        }

        var name = GetString(fields, "name") ?? id;
        var executables = GetStringList(fields, "executables");
        if (executables.Count == 0)
        {
            return Result<GameInfo>.Fail($"游戏“{id}”缺少 executables（ETW 匹配键，不允许为空）");
        }

        var antiCheatText = GetString(fields, "antiCheat") ?? "None";
        if (!Enum.TryParse<KnownAntiCheat>(antiCheatText.Trim(), ignoreCase: true, out var antiCheat))
        {
            return Result<GameInfo>.Fail(
                $"游戏“{id}”的 antiCheat 无效：{antiCheatText}（None|Vac|EasyAntiCheat|BattlEye|Vanguard|Ricochet|TencentAce|Faceit|Unknown）");
        }

        var tierText = GetString(fields, "recommendedTier") ?? "Unknown";
        if (!Enum.TryParse<HardwareTier>(tierText.Trim(), ignoreCase: true, out var tier))
        {
            return Result<GameInfo>.Fail($"游戏“{id}”的 recommendedTier 无效：{tierText}（Low|Mid|High|Ultra|Unknown）");
        }

        var platforms = new List<GamePlatform>();
        foreach (var platformText in GetStringList(fields, "platforms"))
        {
            if (Enum.TryParse<GamePlatform>(platformText.Trim(), ignoreCase: true, out var platform))
            {
                platforms.Add(platform);
            }
        }

        return Result<GameInfo>.Ok(new GameInfo(
            id.Trim(),
            name.Trim(),
            executables,
            platforms,
            antiCheat,
            tier));
    }

    private static string? GetString(IReadOnlyDictionary<string, object> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, object> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value))
        {
            return Array.Empty<string>();
        }

        if (value is List<object> list)
        {
            var items = list.Select(item => item?.ToString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            return items;
        }

        var single = value?.ToString();
        return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
    }
}
