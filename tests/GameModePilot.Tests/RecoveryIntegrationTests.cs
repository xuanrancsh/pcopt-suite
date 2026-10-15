// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19）崩溃安全：模拟"进程被强杀后重启"，验证开机恢复服务能否仅凭 Journal
/// 还原未闭合会话。对照"静态规则（启动时预注册）"与"动态按游戏规则（仅会话时注册）"。
/// </summary>
public sealed class RecoveryIntegrationTests : IDisposable
{
    private readonly string _root;

    /// <summary>初始化测试（独立临时目录）。</summary>
    public RecoveryIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcopt-gmp-recovery", Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响结论
        }
    }

    [Fact]
    public void Recovery_StaticPreRegisteredRule_ShouldRestoreAfterRestart()
    {
        const string ruleId = RuleSetBuilder.TimerRuleId;

        // 第一次运行：应用规则（Journal 落盘）后"崩溃"。
        var engine1 = BuildEngine(new RudeActionHandler(), out _);
        engine1.RegisterRule(Rule(ruleId, "timer"));
        engine1.ApplyRule(ruleId, SessionEngine.ProductName).IsSuccess.Should().BeTrue();

        // 重启：新的引擎只预注册静态规则（含 timer），恢复服务应能还原。
        var handler2 = new RudeActionHandler();
        var engine2 = BuildEngine(handler2, out var store2);
        engine2.RegisterRule(Rule(ruleId, "timer"));

        var report = Recover(store2, engine2);

        report.IsFullyRecovered.Should().BeTrue(
            $"静态预注册规则 {ruleId} 须能在重启后自动还原；未还原：{string.Join(",", report.FailedRuleIds)}");
        engine2.IsApplied(ruleId, SessionEngine.ProductName).Should().BeFalse();
    }

    [Fact]
    public void Recovery_DynamicIfeoRule_ShouldRestoreAfterRestart()
    {
        // 命中内核级反作弊时，产品 A 会把进程优先级降级为 IFEO 注册表写入，规则 Id 带 .ifeo 后缀；
        // 该规则仅在会话开始时动态注册，且会留下 HKLM 注册表残留 —— 崩溃后应能自动还原。
        const string ifeoRuleId = RuleSetBuilder.GamePriorityRuleId + IfeoFallbackPlanner.IfeoSuffix;

        var engine1 = BuildEngine(new RecordingRegistryHandler(), out _);
        engine1.RegisterRule(IfeoRule(ifeoRuleId));
        engine1.ApplyRule(ifeoRuleId, SessionEngine.ProductName).IsSuccess.Should().BeTrue();

        // 重启：模拟 App.OnStartup 的 RegisterStaticRules（不含动态 .ifeo 规则）。
        var registry = new RecordingRegistryHandler();
        var engine2 = BuildEngine(registry, out var store2);
        engine2.RegisterRule(RuleSetBuilder.BuildPowerRule());
        engine2.RegisterRule(RuleSetBuilder.BuildTimerRule());
        engine2.RegisterRule(RuleSetBuilder.BuildBackgroundPriorityRule());

        var report = Recover(store2, engine2);

        report.IsFullyRecovered.Should().BeTrue(
            $"崩溃后 IFEO 规则 {ifeoRuleId} 的注册表写入须能自动还原（否则 HKLM 残留持续影响该游戏），"
            + $"但被标记为失败：{string.Join(",", report.FailedRuleIds)}");
    }

    private TweakEngine BuildEngine(ITweakActionHandler handler, out JournalStore store)
    {
        store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        return new TweakEngine(
            new[] { handler }, store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
    }

    private static RecoveryReport Recover(JournalStore store, TweakEngine engine)
    {
        var restorer = new TweakEngineActionRestorer(engine);
        var service = new RecoveryService(store, restorer, NullLogger<RecoveryService>.Instance);
        var result = service.RecoverOpenSessions();
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    private static TweakRule Rule(string id, string tag) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Safe,
        DisplayName = id,
        Description = "测试",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new RudeAction(tag) },
    };

    private static TweakRule IfeoRule(string id) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Notice,
        DisplayName = id,
        Description = "测试",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[]
        {
            new RegistrySetAction(
                IfeoPaths.PerfOptions("VALORANT.exe"),
                IfeoFallbackPlanner.CpuPriorityClassValueName,
                IfeoPaths.CpuPriorityClassValues.High,
                RegistryValueKind.DWord),
        },
    };
}
