// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using PcOpt.Core;
using PcOpt.Core.Config;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19 · 第 2 轮）缺陷 B 证伪式复查：还原日志内嵌规则定义快照 —— 动态规则崩溃后
/// 真正可还原、旧日志（无快照字段）兼容、快照 Id 不一致被拒绝、不可序列化动作不中断主流程。
/// </summary>
public sealed class JournalSnapshotCompatibilityTests : IDisposable
{
    private const string DynamicRuleId = RuleSetBuilder.GamePriorityRuleId + IfeoFallbackPlanner.IfeoSuffix;

    private readonly string _root;

    /// <summary>初始化测试（独立临时目录）。</summary>
    public JournalSnapshotCompatibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcopt-gmp-snap", Guid.NewGuid().ToString("N"));
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
    public void ApplyRule_ShouldEmbedRuleDefinitionSnapshot()
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new ProbeRegistryHandler() }, store,
            new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        engine.RegisterRule(IfeoRule(DynamicRuleId));

        engine.ApplyRule(DynamicRuleId, SessionEngine.ProductName).IsSuccess.Should().BeTrue();

        var open = store.ListSessions().SelectMany(s => s.OpenEntries).ToList();
        open.Should().ContainSingle();
        open[0].RuleDefinitionYaml.Should().NotBeNullOrWhiteSpace("动态规则须把定义快照写入还原日志");
        open[0].RuleDefinitionYaml.Should().Contain(DynamicRuleId);
        open[0].RuleDefinitionYaml.Should().Contain("RegistrySet");
    }

    [Fact]
    public void ApplyRule_NonSerializableAction_ShouldStillApply_WithoutSnapshot()
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new RudeActionHandler() }, store,
            new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        var rule = RuleWithAction("rude.rule", new RudeAction("x"));

        engine.RegisterRule(rule);
        var apply = engine.ApplyRule("rude.rule", SessionEngine.ProductName);

        apply.IsSuccess.Should().BeTrue("不可序列化的动作只应跳过快照，绝不中断应用");
        var open = store.ListSessions().SelectMany(s => s.OpenEntries).ToList();
        open[0].RuleDefinitionYaml.Should().BeNull();
    }

    [Fact]
    public void RestoreEntry_UnloadedRuleWithoutSnapshot_ShouldFailCleanly_NotThrow()
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(), store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);

        var act = () => engine.RestoreEntry(new JournalEntry("ghost.rule", 0, "{}", DateTime.UtcNow));

        act.Should().NotThrow();
        act().IsSuccess.Should().BeFalse();
        act().Error.Should().Contain("尚未加载");
    }

    [Fact]
    public void JournalStore_Load_LegacyJsonWithoutSnapshotField_ShouldDeserialize()
    {
        var legacyJson = """
        {
          "SessionId": "abc123",
          "Product": "GameModePilot",
          "StartedAt": "2026-01-01T00:00:00Z",
          "Status": "Open",
          "Entries": [
            {
              "RuleId": "legacy.rule",
              "ActionIndex": 0,
              "OriginalValueJson": "{}",
              "AppliedAt": "2026-01-01T00:00:00Z",
              "RevertedAt": null
            }
          ]
        }
        """;
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "abc123.json"), legacyJson);

        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var load = store.Load("abc123");

        load.IsSuccess.Should().BeTrue(load.Error);
        load.Value.Should().NotBeNull();
        load.Value!.OpenEntries.Should().ContainSingle()
            .Subject.RuleDefinitionYaml.Should().BeNull("旧日志无该字段时应兼容为 null");
    }

    [Fact]
    public void RestoreEntry_SnapshotIdMismatch_ShouldBeRejected()
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new ProbeRegistryHandler() }, store,
            new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);

        // 快照定义的是"另一个规则"，条目 RuleId 与之不符 → 必须拒绝，不得凭异源定义还原
        var foreignYaml = TweakRuleWriter.Write(IfeoRule("some.other.rule"));
        var entry = new JournalEntry("expected.rule", 0, "{}", DateTime.UtcNow, revertedAt: null, ruleDefinitionYaml: foreignYaml);

        var result = engine.RestoreEntry(entry);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("尚未加载");
    }

    [Fact]
    public void Recovery_AfterRestart_ShouldRebuildDynamicRule_AndTrulyRevert()
    {
        // 第一次运行：应用动态 IFEO 规则（写入 HKLM 类注册表内容由探测器代记）后"崩溃"
        var store1 = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var probe1 = new ProbeRegistryHandler();
        var engine1 = new TweakEngine(
            new ITweakActionHandler[] { probe1 }, store1, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        engine1.RegisterRule(IfeoRule(DynamicRuleId));
        engine1.ApplyRule(DynamicRuleId, SessionEngine.ProductName).IsSuccess.Should().BeTrue();

        // 重启：全新引擎，不预注册该动态规则
        var store2 = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var probe2 = new ProbeRegistryHandler();
        var engine2 = new TweakEngine(
            new ITweakActionHandler[] { probe2 }, store2, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);

        var report = new RecoveryService(
                store2, new TweakEngineActionRestorer(engine2), NullLogger<RecoveryService>.Instance)
            .RecoverOpenSessions();

        report.IsSuccess.Should().BeTrue(report.Error);
        report.Value!.IsFullyRecovered.Should().BeTrue(
            $"动态规则 {DynamicRuleId} 应凭日志快照重建并还原；失败项：{string.Join(",", report.Value.FailedRuleIds)}");

        // 关键：还原动作须真实执行（而非仅"测试变绿"）
        probe2.Calls.Should().Contain(c => c.Method == "Revert"
            && c.Path == IfeoPaths.PerfOptions("VALORANT.exe"));
        engine2.IsApplied(DynamicRuleId, SessionEngine.ProductName).Should().BeFalse();
    }

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

    private static TweakRule RuleWithAction(string id, TweakAction action) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Safe,
        DisplayName = id,
        Description = "测试",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { action },
    };

    /// <summary>记录注册表动作调用（含 Revert）的处理器，用于证实"还原确实发生"。</summary>
    private sealed class ProbeRegistryHandler : ITweakActionHandler
    {
        /// <summary>调用记录（方法名 + 注册表路径）。</summary>
        public List<(string Method, string Path)> Calls { get; } = new();

        /// <inheritdoc/>
        public bool CanHandle(TweakAction action) => action is RegistrySetAction;

        /// <inheritdoc/>
        public Result<string> CaptureOriginal(TweakAction action)
        {
            Calls.Add(("CaptureOriginal", ((RegistrySetAction)action).Path));
            return Result<string>.Ok("{}");
        }

        /// <inheritdoc/>
        public Result Apply(TweakAction action)
        {
            Calls.Add(("Apply", ((RegistrySetAction)action).Path));
            return Result.Ok();
        }

        /// <inheritdoc/>
        public Result Revert(TweakAction action, string originalValueJson)
        {
            Calls.Add(("Revert", ((RegistrySetAction)action).Path));
            return Result.Ok();
        }

        /// <inheritdoc/>
        public Result Verify(TweakAction action) => Result.Ok();
    }
}
