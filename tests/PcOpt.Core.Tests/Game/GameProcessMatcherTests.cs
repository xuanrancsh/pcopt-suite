// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Game;
using PcOpt.Core.Tests.TestDoubles;

namespace PcOpt.Core.Tests.Game;

/// <summary>
/// <see cref="GameProcessMatcher"/> 测试（T8）：
/// 50 个常见软件零误触发 + 库内游戏命中 + 大小写/路径容错（架构 3.4）。
/// </summary>
public sealed class GameProcessMatcherTests
{
    private readonly GameProcessMatcher _matcher;

    public GameProcessMatcherTests()
    {
        var repoRoot = FindRepositoryRoot();
        var database = GameDatabase.LoadFromFile(Path.Combine(repoRoot, "rules", "games.yaml"));
        database.IsSuccess.Should().BeTrue(database.Error);
        _matcher = new GameProcessMatcher(database.Value!);
    }

    [Fact]
    public void Match_FiftyCommonSoftwareNames_ShouldNeverTrigger()
    {
        // 零误触发原则：常见非游戏软件（浏览器 / IDE / 办公 / 系统 / 通讯）绝不命中
        var commonSoftware = new[]
        {
            "explorer.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe",
            "notepad.exe", "calc.exe", "mspaint.exe", "taskmgr.exe", "regedit.exe", "mmc.exe",
            "chrome.exe", "msedge.exe", "firefox.exe", "iexplore.exe", "opera.exe", "brave.exe",
            "Code.exe", "devenv.exe", "idea64.exe", "pycharm64.exe", "sublime_text.exe", "notepad++.exe",
            "WINWORD.EXE", "EXCEL.EXE", "POWERPNT.EXE", "OUTLOOK.EXE", "ONENOTE.EXE", "ACCESS.EXE",
            "Photoshop.exe", "Illustrator.exe", "Lightroom.exe", "Premiere.exe", "AfterFX.exe",
            "WeChat.exe", "QQ.exe", "TIM.exe", "DingTalk.exe", "Feishu.exe", "Slack.exe", "Zoom.exe",
            "steam.exe", "epicgameslauncher.exe", "battle.net.exe", "galaxyclient.exe", "ubisoftconnect.exe",
            "dotnet.exe", "WindowsTerminal.exe", "SearchUI.exe", "RuntimeBroker.exe", "SystemSettings.exe",
        };

        foreach (var executable in commonSoftware)
        {
            var match = _matcher.Match(new ProcessEventInfo(1234, $@"C:\Program Files\{executable}", null));
            match.Should().BeNull($"常见软件 {executable} 绝不误触发");
        }
    }

    [Theory]
    [InlineData("cs2.exe", "cs2")]
    [InlineData("VALORANT.exe", "valorant")]
    [InlineData("TslGame.exe", "pubg")]
    [InlineData("eldenring.exe", "eldenring")]
    [InlineData("NarakaBladepoint.exe", "naraka")]
    [InlineData("FortniteClient-Win64-Shipping.exe", "fortnite")]
    [InlineData("Warframe.x64.exe", "warframe")]
    public void Match_KnownGames_ShouldHit(string executable, string expectedGameId)
    {
        var match = _matcher.Match(new ProcessEventInfo(5678, $@"D:\Games\{executable}", null));

        match.Should().NotBeNull();
        match!.Game.Id.Should().Be(expectedGameId);
        match.ProcessId.Should().Be(5678);
        match.MatchKind.Should().Be(GameMatchKind.ExecutableName);
    }

    [Theory]
    [InlineData(@"D:\Steam\steamapps\common\Counter-Strike Global Offensive\cs2.exe")]
    [InlineData(@"d:\steam\steamapps\common\counter-strike global offensive\CS2.EXE")]
    [InlineData("cs2")]
    [InlineData("CS2")]
    public void Match_ShouldTolerateCaseAndPathDifferences(string executablePathOrName)
    {
        var match = _matcher.Match(new ProcessEventInfo(1, executablePathOrName, null));

        match.Should().NotBeNull();
        match!.Game.Id.Should().Be("cs2");
    }

    [Fact]
    public void Match_EmptyPath_ShouldReturnNull()
    {
        _matcher.Match(new ProcessEventInfo(1, "", null)).Should().BeNull();
        _matcher.Match(new ProcessEventInfo(1, "   ", null)).Should().BeNull();
    }

    [Fact]
    public void GameProcessWatcher_InitialState_ShouldNotBeRunning()
    {
        using var watcher = new GameProcessWatcher(_matcher, NullLogger<GameProcessWatcher>.Instance);

        watcher.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void GameProcessWatcher_StopWithoutStart_ShouldBeNoOp()
    {
        using var watcher = new GameProcessWatcher(_matcher, NullLogger<GameProcessWatcher>.Instance);

        var act = async () => await watcher.StopAsync();

        act.Should().NotThrowAsync();
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

/// <summary>固定匹配结果替身（验证监视器事件桥接，不依赖真实 ETW）。</summary>
internal sealed class FakeProcessMatcher : IProcessMatcher
{
    private readonly GameMatchResult? _result;

    /// <summary>初始化替身。</summary>
    public FakeProcessMatcher(GameMatchResult? result)
    {
        _result = result;
    }

    /// <inheritdoc/>
    public GameMatchResult? Match(ProcessEventInfo info) => _result;
}
