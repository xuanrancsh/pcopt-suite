// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Game;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19）守护服务：启用 / 暂停 / 定时自动恢复语义（托盘"暂停 1 小时"须为到期自动恢复，非永久停用）。
/// 仅构造对象，不启动 ETW（需要管理员权限）。
/// </summary>
public sealed class GuardianServiceTests
{
    [Fact]
    public void Default_ShouldBeEnabledAndGuarding()
    {
        using var harness = Create();

        harness.Guardian.IsEnabled.Should().BeTrue();
        harness.Guardian.ShouldGuard.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_ShouldStopGuarding_AndEnableResumes()
    {
        using var harness = Create();

        await harness.Guardian.DisableGuardianAsync();
        harness.Guardian.IsEnabled.Should().BeFalse();
        harness.Guardian.ShouldGuard.Should().BeFalse();

        await harness.Guardian.EnableGuardianAsync(null);
        harness.Guardian.IsEnabled.Should().BeTrue();
        harness.Guardian.ShouldGuard.Should().BeTrue();
    }

    [Fact]
    public async Task PauseFor_Timed_ShouldAutoResume_AndKeepMasterSwitchOn()
    {
        using var harness = Create();

        harness.Guardian.PauseFor(TimeSpan.FromMilliseconds(40));

        // 暂停期间不守护
        harness.Guardian.ShouldGuard.Should().BeFalse();
        // 总开关不受影响（"暂停"不等于"停用"）
        harness.Guardian.IsEnabled.Should().BeTrue();

        await Task.Delay(140);

        // 到期自动恢复
        harness.Guardian.ShouldGuard.Should().BeTrue("托盘“暂停 1 小时”语义须为到期自动恢复，而非永久停用");
    }

    [Fact]
    public void PauseFor_NonPositive_ShouldPauseUntilExplicitResume_PerContract()
    {
        using var harness = Create();

        // 契约（GuardianService.PauseFor 文档）：时长 ≤ 0 表示"暂停到显式恢复"
        harness.Guardian.PauseFor(TimeSpan.Zero);

        harness.Guardian.ShouldGuard.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_ShouldReflectGuardingPhase()
    {
        using var harness = Create();

        var status = await harness.Guardian.GetStatusAsync();

        status.GuardianEnabled.Should().BeTrue();
        status.Phase.Should().Be(SessionPhase.Guarding.ToString());
    }

    private static Harness Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "pcopt-gmp-guardian", Guid.NewGuid().ToString("N"));
        var store = new JournalStore(NullLogger<JournalStore>.Instance, root);
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(), store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        var session = new SessionEngine(
            engine, new FakeAntiCheatDetector(), store,
            new SessionHistoryStore(NullLogger<SessionHistoryStore>.Instance, root),
            NullLogger<SessionEngine>.Instance);

        var watcher = new GameProcessWatcher(new NullProcessMatcher(), NullLogger<GameProcessWatcher>.Instance);
        var scanner = new GameLibraryScanner(
            new StaticGameSourcePaths(), new EmptyUninstallReader(), NullLogger<GameLibraryScanner>.Instance);
        var gameWatcher = new GameWatcher(watcher, scanner, NullLogger<GameWatcher>.Instance);

        var guardian = new GuardianService(
            gameWatcher, session, new TweakPreferences(), NullLogger<GuardianService>.Instance);
        return new Harness(gameWatcher, guardian, root);
    }

    private sealed record Harness(GameWatcher Watcher, GuardianService Guardian, string Root) : IDisposable
    {
        public void Dispose()
        {
            Watcher.Dispose();
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }

    private sealed class EmptyUninstallReader : IUninstallRegistryReader
    {
        public IReadOnlyList<UninstallEntry> ReadEntries() => Array.Empty<UninstallEntry>();
    }
}
