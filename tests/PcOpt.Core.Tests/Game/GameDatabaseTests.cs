// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Game;

namespace PcOpt.Core.Tests.Game;

/// <summary><see cref="GameDatabase"/> 测试（T7：rules/games.yaml ≥60 款 + 可执行名匹配）。</summary>
public sealed class GameDatabaseTests
{
    private static GameDatabase LoadRepositoryDatabase()
    {
        var repoRoot = FindRepositoryRoot();
        var result = GameDatabase.LoadFromFile(Path.Combine(repoRoot, "rules", "games.yaml"));
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    [Fact]
    public void LoadFromFile_RepositoryDatabase_ShouldMeetMinimumCount()
    {
        var database = LoadRepositoryDatabase();

        // 架构 3.4 验收线：首发 ≥ 60 款
        database.Games.Count.Should().BeGreaterThanOrEqualTo(GameDatabase.MinimumGameCount);
    }

    [Fact]
    public void RepositoryDatabase_AllEntries_ShouldHaveUniqueIdsAndExecutables()
    {
        var database = LoadRepositoryDatabase();

        database.Games.Select(g => g.Id).Should().OnlyHaveUniqueItems();
        database.Games.Should().OnlyContain(g => g.Executables.Count > 0);
        database.Games.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Name));
    }

    [Theory]
    [InlineData("cs2.exe")]
    [InlineData("CS2.EXE")]
    [InlineData("cs2")]
    [InlineData(@"D:\Steam\steamapps\common\Counter-Strike Global Offensive\cs2.exe")]
    public void FindByExecutable_ShouldBeCaseAndPathInsensitive(string executableNameOrPath)
    {
        var database = LoadRepositoryDatabase();

        var game = database.FindByExecutable(executableNameOrPath);

        game.Should().NotBeNull();
        game!.Id.Should().Be("cs2");
        game.Platforms.Should().Contain(GamePlatform.Steam);
    }

    [Fact]
    public void FindByExecutable_KnownGames_ShouldHit()
    {
        var database = LoadRepositoryDatabase();

        database.FindByExecutable("TslGame.exe")!.Id.Should().Be("pubg");
        database.FindByExecutable("VALORANT.exe")!.Id.Should().Be("valorant");
        database.FindByExecutable("eldenring.exe")!.AntiCheat.Should().Be(KnownAntiCheat.EasyAntiCheat);
        database.FindByExecutable("NarakaBladepoint.exe")!.AntiCheat.Should().Be(KnownAntiCheat.BattlEye);
        database.FindByExecutable("DNF.exe")!.AntiCheat.Should().Be(KnownAntiCheat.TencentAce);
    }

    [Fact]
    public void FindByExecutable_CommonSoftware_ShouldMiss()
    {
        var database = LoadRepositoryDatabase();

        // 常见非游戏软件绝不命中（ETW 零误触发的前提）
        database.FindByExecutable("explorer.exe").Should().BeNull();
        database.FindByExecutable("chrome.exe").Should().BeNull();
        database.FindByExecutable("Code.exe").Should().BeNull();
        database.FindByExecutable("notepad.exe").Should().BeNull();
        database.FindByExecutable("steam.exe").Should().BeNull();
        database.FindByExecutable("dotnet.exe").Should().BeNull();
    }

    [Fact]
    public void FindById_ShouldWork()
    {
        var database = LoadRepositoryDatabase();

        database.FindById("gta5")!.Name.Should().Be("侠盗猎车手 5");
        database.FindById("GTA5")!.Id.Should().Be("gta5");
        database.FindById("no.such.game").Should().BeNull();
        database.FindById(null).Should().BeNull();
    }

    [Fact]
    public void LoadFromYaml_InvalidContent_ShouldFail()
    {
        var result = GameDatabase.LoadFromYaml("- id: x.y\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("executables");
    }

    [Fact]
    public void LoadFromYaml_DuplicateId_ShouldFail()
    {
        var yaml = """
            - id: a.b
              name: 甲
              executables: [a.exe]
            - id: a.b
              name: 乙
              executables: [b.exe]
            """;

        var result = GameDatabase.LoadFromYaml(yaml);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("重复");
    }

    [Fact]
    public void LoadFromYaml_InvalidAntiCheat_ShouldFail()
    {
        var yaml = """
            - id: a.b
              name: 甲
              executables: [a.exe]
              antiCheat: 不存在的东西
            """;

        var result = GameDatabase.LoadFromYaml(yaml);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("antiCheat");
    }

    [Fact]
    public void LoadFromYaml_Empty_ShouldFail()
    {
        GameDatabase.LoadFromYaml("").IsSuccess.Should().BeFalse();
        GameDatabase.LoadFromYaml("   ").IsSuccess.Should().BeFalse();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "pcopt-suite.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("未找到仓库根目录（pcopt-suite.sln）");
    }
}
