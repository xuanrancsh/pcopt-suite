// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Game;
using PcOpt.Core.Tests.TestDoubles;

namespace PcOpt.Core.Tests.Game;

/// <summary>
/// <see cref="GameLibraryScanner"/> 测试（T7）：构造假目录树验证 Steam / Epic / 注册表类平台解析。
/// 全部 IO 在测试临时目录内，只读，不触碰真实系统（合规红线 #3）。
/// </summary>
public sealed class GameLibraryScannerTests : IDisposable
{
    private readonly string _root;

    public GameLibraryScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcopt-game-scan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---------- Steam ----------

    [Fact]
    public void ScanSteamLibraries_WithFakeTree_ShouldParseManifests()
    {
        // 主库 + 从 libraryfolders.vdf 声明的第二库
        var steamRoot = CreateDirectory("Steam");
        var secondLibrary = CreateDirectory("SteamLibrary2");
        WriteAllText(
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            """
            "libraryfolders"
            {
                "0" { "path" "STEAM_ROOT" }
                "1" { "path" "SECOND_LIBRARY" }
            }
            """.Replace("STEAM_ROOT", steamRoot.Replace(@"\", @"\\")).Replace("SECOND_LIBRARY", secondLibrary.Replace(@"\", @"\\")));

        WriteAllText(
            Path.Combine(steamRoot, "steamapps", "appmanifest_730.acf"),
            """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "installdir" "Counter-Strike Global Offensive"
            }
            """);
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps", "common", "Counter-Strike Global Offensive"));

        WriteAllText(
            Path.Combine(secondLibrary, "steamapps", "appmanifest_570.acf"),
            """
            "AppState"
            {
                "appid"     "570"
                "name"      "Dota 2"
                "installdir" "dota 2 beta"
            }
            """);
        Directory.CreateDirectory(Path.Combine(secondLibrary, "steamapps", "common", "dota 2 beta"));

        var scanner = CreateScanner(new StaticGameSourcePaths(steamInstallPath: steamRoot));

        var result = scanner.ScanSteamLibraries();

        result.IsSuccess.Should().BeTrue(result.Error);
        var games = result.Value!;
        games.Should().HaveCount(2);

        var cs2 = games.Single(g => g.GameId == "730");
        cs2.Name.Should().Be("Counter-Strike 2");
        cs2.Platform.Should().Be(GamePlatform.Steam);
        cs2.Confidence.Should().Be(GameDetectionConfidence.Confirmed);
        cs2.InstallPath.Should().Be(Path.Combine(steamRoot, "steamapps", "common", "Counter-Strike Global Offensive"));

        var dota = games.Single(g => g.GameId == "570");
        dota.InstallPath.Should().Be(Path.Combine(secondLibrary, "steamapps", "common", "dota 2 beta"));
    }

    [Fact]
    public void ScanSteamLibraries_CorruptLibraryFolders_ShouldStillScanInstallLibrary()
    {
        var steamRoot = CreateDirectory("Steam");
        WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), "这不是 VDF");
        WriteAllText(
            Path.Combine(steamRoot, "steamapps", "appmanifest_440.acf"),
            """
            "AppState"
            {
                "appid"     "440"
                "name"      "Team Fortress 2"
                "installdir" "Team Fortress 2"
            }
            """);

        var scanner = CreateScanner(new StaticGameSourcePaths(steamInstallPath: steamRoot));

        var result = scanner.ScanSteamLibraries();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Should().ContainSingle(g => g.GameId == "440");
    }

    [Fact]
    public void ScanSteamLibraries_MissingInstallDir_ShouldReportNullInstallPath()
    {
        var steamRoot = CreateDirectory("Steam");
        WriteAllText(
            Path.Combine(steamRoot, "steamapps", "appmanifest_999.acf"),
            """
            "AppState"
            {
                "appid"     "999"
                "name"      "未下载完成的游戏"
                "installdir" "ghost-folder"
            }
            """);

        var scanner = CreateScanner(new StaticGameSourcePaths(steamInstallPath: steamRoot));

        var game = scanner.ScanSteamLibraries().Value!.Single();

        // 清单在但目录不在：如实报告 null，不猜测路径
        game.InstallPath.Should().BeNull();
        game.Name.Should().Be("未下载完成的游戏");
    }

    [Fact]
    public void ScanSteamLibraries_DuplicateManifestAcrossLibraries_ShouldDedupeByAppId()
    {
        var installRoot = CreateDirectory("Steam");
        var extra = CreateDirectory("SteamExtra");
        var manifest = """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "installdir" "cs2"
            }
            """;
        WriteAllText(Path.Combine(installRoot, "steamapps", "appmanifest_730.acf"), manifest);
        WriteAllText(Path.Combine(extra, "steamapps", "appmanifest_730.acf"), manifest);

        var scanner = CreateScanner(new StaticGameSourcePaths(
            steamInstallPath: installRoot,
            extraSteamLibraryPaths: new[] { extra }));

        scanner.ScanSteamLibraries().Value!.Should().ContainSingle(g => g.GameId == "730");
    }

    [Fact]
    public void ScanSteamLibraries_NoSteam_ShouldReturnEmpty()
    {
        var scanner = CreateScanner(new StaticGameSourcePaths());

        var result = scanner.ScanSteamLibraries();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
    }

    // ---------- Epic ----------

    [Fact]
    public void ScanEpicLibrary_WithFakeManifests_ShouldParseItems()
    {
        var manifests = CreateDirectory("EpicManifests");
        WriteAllText(
            Path.Combine(manifests, "a1b2c3.item"),
            """
            {
                "AppName": "Fortnite",
                "DisplayName": "堡垒之夜",
                "InstallLocation": "INSTALL_DIR",
                "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteClient-Win64-Shipping.exe"
            }
            """.Replace("INSTALL_DIR", CreateDirectory("FortniteInstall").Replace(@"\", @"\\")));
        WriteAllText(Path.Combine(manifests, "broken.item"), "{ 这不是 JSON");

        var scanner = CreateScanner(new StaticGameSourcePaths(epicManifestsDirectory: manifests));

        var result = scanner.ScanEpicLibrary();

        result.IsSuccess.Should().BeTrue(result.Error);
        var games = result.Value!;
        games.Should().ContainSingle();

        var fortnite = games[0];
        fortnite.GameId.Should().Be("Fortnite");
        fortnite.Name.Should().Be("堡垒之夜");
        fortnite.Platform.Should().Be(GamePlatform.Epic);
        fortnite.Confidence.Should().Be(GameDetectionConfidence.Confirmed);
        fortnite.LaunchExecutable.Should().Contain("FortniteClient-Win64-Shipping.exe");
        fortnite.InstallPath.Should().NotBeNull();
    }

    [Fact]
    public void ScanEpicLibrary_NoEpic_ShouldReturnEmpty()
    {
        var scanner = CreateScanner(new StaticGameSourcePaths());

        scanner.ScanEpicLibrary().Value!.Should().BeEmpty();
    }

    // ---------- 注册表类平台（GOG / Xbox / WeGame） ----------

    [Fact]
    public void ScanRegistryPlatforms_ShouldClassifyByPublisherMarkers()
    {
        var reader = new FakeUninstallRegistryReader(new[]
        {
            new UninstallEntry("gog_game_1", "赛博朋克 2077", "GOG.com", CreateDirectory("GogGame"), "https://gog.com/game/cyberpunk_2077"),
            new UninstallEntry("xbox_game_1", "Forza Horizon 5", "Microsoft Game Pass", CreateDirectory("XboxGame"), null),
            new UninstallEntry("wegame_game_1", "英雄联盟", "腾讯游戏", null, null),
            new UninstallEntry("random_tool", "某解压软件", "某软件公司", null, null),
        });

        var scanner = CreateScanner(new StaticGameSourcePaths(), reader);

        var result = scanner.ScanRegistryPlatforms();

        result.IsSuccess.Should().BeTrue(result.Error);
        var games = result.Value!;

        // 非游戏条目不归入（探测不到不猜测）
        games.Should().HaveCount(3);
        games.Should().NotContain(g => g.Name == "某解压软件");

        var gog = games.Single(g => g.Platform == GamePlatform.Gog);
        gog.Confidence.Should().Be(GameDetectionConfidence.Inferred);
        gog.InstallPath.Should().NotBeNull();

        games.Single(g => g.Platform == GamePlatform.Xbox).Name.Should().Be("Forza Horizon 5");
        games.Single(g => g.Platform == GamePlatform.WeGame).Name.Should().Be("英雄联盟");

        // WeGame 条目 InstallLocation 为空 → null（不猜路径）
        games.Single(g => g.Platform == GamePlatform.WeGame).InstallPath.Should().BeNull();
    }

    [Fact]
    public void ScanRegistryPlatforms_EntryWithoutDisplayName_ShouldBeSkipped()
    {
        var reader = new FakeUninstallRegistryReader(new[]
        {
            new UninstallEntry("no_name", null, "GOG.com", null, null),
        });

        var scanner = CreateScanner(new StaticGameSourcePaths(), reader);

        scanner.ScanRegistryPlatforms().Value!.Should().BeEmpty();
    }

    // ---------- 聚合 ----------

    [Fact]
    public async Task ScanAsync_ShouldAggregateAllPlatforms_AndTolerateSinglePlatformFailure()
    {
        var steamRoot = CreateDirectory("Steam");
        WriteAllText(
            Path.Combine(steamRoot, "steamapps", "appmanifest_730.acf"),
            """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "installdir" "cs2"
            }
            """);
        var reader = new FakeUninstallRegistryReader(new[]
        {
            new UninstallEntry("gog_1", "巫师 3", "GOG.com", null, null),
        });

        var scanner = CreateScanner(new StaticGameSourcePaths(steamInstallPath: steamRoot), reader);

        var result = await scanner.ScanAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var report = result.Value!;
        report.Games.Should().HaveCount(2);
        report.PlatformErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanAsync_WithCancellation_ShouldFail()
    {
        var scanner = CreateScanner(new StaticGameSourcePaths());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await scanner.ScanAsync(cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("取消");
    }

    // ---------- 辅助 ----------

    private GameLibraryScanner CreateScanner(
        IGameSourcePaths paths,
        IUninstallRegistryReader? reader = null) =>
        new(paths, reader ?? new FakeUninstallRegistryReader(Array.Empty<UninstallEntry>()), NullLogger<GameLibraryScanner>.Instance);

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_root, name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteAllText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
