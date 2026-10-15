// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// 内置最小引擎「经内核 Journal 可还原 + 崩溃恢复」独立验证（QA 构造，T37 重点 7 / C-P0-5）。
/// 用替身处理器记录动作（不触碰真实系统），验证 apply → Journal 落盘 → revert / 崩溃恢复 的闭环。
/// </summary>
public sealed class MinimalEngineJournalTests
{
    private static (TweakEngine Engine, JournalStore Store, MinimalOptimizationEngine Minimal, FakeMemoryCleaner Memory, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "framelab-min-" + Guid.NewGuid().ToString("N"));
        var store = new JournalStore(NullLogger<JournalStore>.Instance, root);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new FakeTweakActionHandler() },
            store,
            new WindowsSystemEnvironment(),
            NullLogger<TweakEngine>.Instance);
        var memory = new FakeMemoryCleaner();
        var minimal = new MinimalOptimizationEngine(engine, memory, NullLogger<MinimalOptimizationEngine>.Instance);
        return (engine, store, minimal, memory, root);
    }

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响断言
        }
    }

    [Fact]
    public void MinimalRule_AllActionsAutoRevertible()
    {
        var rule = MinimalOptimizationEngine.BuildRule();
        rule.Actions.Should().OnlyContain(action => OfficialRulePolicy.IsAutoRevertible(action),
            "内置最小引擎的动作必须全部可自动还原（合规红线 #9）");
        OfficialRulePolicy.ValidateRule(rule).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Apply_WritesJournalEntries_ThenRevert_ClosesThem()
    {
        var (engine, store, minimal, memory, root) = Build();
        try
        {
            var apply = minimal.Apply();
            apply.IsSuccess.Should().BeTrue(apply.Error);
            memory.PurgeCount.Should().Be(1);
            minimal.IsApplied.Should().BeTrue();

            var openSession = store.ListSessions().Single();
            openSession.OpenEntries.Count().Should().Be(2, "电源 + 计时器两个动作都应先写 Journal");

            var revert = minimal.Revert();
            revert.IsSuccess.Should().BeTrue(revert.Error);
            minimal.IsApplied.Should().BeFalse();
            store.ListSessions().Should().NotContain(s => s.OpenEntries.Any(), "还原后不应残留未还原条目");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void CrashThenRecovery_RestoresMinimalEngine()
    {
        var (engine, store, minimal, _, root) = Build();
        try
        {
            minimal.Apply().IsSuccess.Should().BeTrue();
            minimal.IsApplied.Should().BeTrue();

            // 模拟崩溃：不调用 Revert，直接启动开机恢复
            var recovery = new RecoveryService(
                store,
                new TweakEngineActionRestorer(engine),
                NullLogger<RecoveryService>.Instance);

            var report = recovery.RecoverOpenSessions();

            report.IsSuccess.Should().BeTrue(report.Error);
            report.Value!.IsFullyRecovered.Should().BeTrue();
            report.Value.EntriesRestored.Should().Be(2);
            minimal.IsApplied.Should().BeFalse("崩溃恢复后最小引擎不应仍生效");
        }
        finally
        {
            Cleanup(root);
        }
    }
}
