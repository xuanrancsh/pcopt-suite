// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>
/// "应用 → 还原"一致性与崩溃恢复的独立验收（QA 构造，T30）。
/// 覆盖产品 B 首发规则包出现过的<b>全部动作类型</b>（RegistrySet / ServiceStartType /
/// ScheduledTask / PowerCfg / TimerResolution / ProcessPriority）：
/// 使用"通吃型"替身处理器，验证引擎对真实 68 条规则的
/// 快照先于执行、应用幂等、LIFO 还原、崩溃恢复 100% 复原，全程不触碰真实系统。
/// </summary>
public sealed class RestoreConsistencyTests
{
    /// <summary>可处理任意动作类型的替身处理器（只记录调用，不触碰系统）。</summary>
    private sealed class UniversalFakeHandler : ITweakActionHandler
    {
        /// <summary>调用记录（按发生顺序）。</summary>
        public List<(string Method, TweakAction Action)> Calls { get; } = new();

        /// <inheritdoc/>
        public bool CanHandle(TweakAction action) => true;

        /// <inheritdoc/>
        public Result<string> CaptureOriginal(TweakAction action)
        {
            Calls.Add(("CaptureOriginal", action));
            return Result<string>.Ok("{\"orig\":null}");
        }

        /// <inheritdoc/>
        public Result Apply(TweakAction action)
        {
            Calls.Add(("Apply", action));
            return Result.Ok();
        }

        /// <inheritdoc/>
        public Result Revert(TweakAction action, string originalValueJson)
        {
            Calls.Add(("Revert", action));
            return Result.Ok();
        }

        /// <inheritdoc/>
        public Result Verify(TweakAction action)
        {
            Calls.Add(("Verify", action));
            return Result.Ok();
        }
    }

    private static (TweakEngine Engine, RuleRepository Repository, JournalStore Store, UniversalFakeHandler Handler)
        LoadRealPackage(TempDirectory temp)
    {
        var verifier = RuleSignatureVerifier.CreateFromFile(RepoLayout.PublicKeyPath);
        verifier.IsSuccess.Should().BeTrue(verifier.Error);

        var handler = new UniversalFakeHandler();
        var store = new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal"));
        var engine = new TweakEngine(
            new ITweakActionHandler[] { handler },
            store,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance,
            verifier.Value);

        var repository = new RuleRepository(engine, RepoLayout.TweaksDirectory, RepoLayout.PublicKeyPath, NullLogger<RuleRepository>.Instance);
        var report = repository.LoadPackage();
        report.IsSuccess.Should().BeTrue(report.Error);
        report.Value!.Failed.Should().Be(0, string.Join("；", report.Value.Failures));
        repository.Count.Should().BeGreaterThanOrEqualTo(60);

        return (engine, repository, store, handler);
    }

    private static int TotalActions(RuleRepository repository) => repository.Rules.Sum(rule => rule.Actions.Length);

    [Fact]
    public void ApplyThenRevertAll_OverRealRules_LeavesNoResidue()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store, _) = LoadRealPackage(temp);
        var product = RuleRepository.ProductName;

        foreach (var rule in repository.Rules)
        {
            var apply = engine.ApplyRule(rule.Id, product);
            apply.IsSuccess.Should().BeTrue($"规则 {rule.Id} 应能应用：{apply.Error}");
        }

        repository.CountApplied().Should().Be(repository.Count, "全部规则应处于已应用状态");

        var revertAll = repository.RevertAll();
        revertAll.IsSuccess.Should().BeTrue(revertAll.Error);
        revertAll.Value!.FullyReverted.Should().BeTrue();
        revertAll.Value.RevertedRules.Should().Be(repository.Count);
        revertAll.Value.RevertedEntries.Should().Be(TotalActions(repository));

        repository.CountApplied().Should().Be(0, "全部还原后不应残留已应用规则");
        store.ListSessions().Should().NotContain(session => session.OpenEntries.Any(),
            "全部还原后不应残留未还原条目（无残留）");
    }

    [Fact]
    public void RecoveryAfterCrash_RestoresEveryAction()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store, _) = LoadRealPackage(temp);
        var product = RuleRepository.ProductName;

        foreach (var rule in repository.Rules)
        {
            engine.ApplyRule(rule.Id, product).IsSuccess.Should().BeTrue();
        }

        var expectedActions = TotalActions(repository);

        // 模拟崩溃：不调用任何 Revert，直接启动"开机恢复"。
        var recovery = new RecoveryService(
            store,
            new TweakEngineActionRestorer(engine),
            NullLogger<RecoveryService>.Instance);

        var report = recovery.RecoverOpenSessions();

        report.IsSuccess.Should().BeTrue(report.Error);
        report.Value!.IsFullyRecovered.Should().BeTrue("崩溃恢复应 100% 完成，无失败、无遗留");
        report.Value.EntriesRestored.Should().Be(expectedActions, "每个动作条目都应被还原");

        foreach (var rule in repository.Rules)
        {
            engine.IsApplied(rule.Id, product).Should().BeFalse($"恢复后规则 {rule.Id} 不应仍处于已应用状态");
        }

        store.ListSessions().Should().NotContain(session => session.OpenEntries.Any());
    }

    [Fact]
    public void ApplyRule_Twice_IsIdempotent_NoDoubleSnapshot()
    {
        using var temp = new TempDirectory();
        var (engine, repository, _, handler) = LoadRealPackage(temp);
        var product = RuleRepository.ProductName;
        var rule = repository.Rules.First();

        var first = engine.ApplyRule(rule.Id, product);
        first.IsSuccess.Should().BeTrue();
        first.Value!.WasApplied.Should().BeTrue();

        var callsAfterFirst = handler.Calls.Count;

        var second = engine.ApplyRule(rule.Id, product);
        second.IsSuccess.Should().BeTrue();
        second.Value!.AlreadyApplied.Should().BeTrue("重复应用应命中幂等，不重复执行");
        second.Value.WasApplied.Should().BeFalse();
        handler.Calls.Count.Should().Be(callsAfterFirst, "幂等命中时不应再次产生任何 handler 调用");
    }

    [Fact]
    public void RevertRule_NeverApplied_IsNoOpSuccess()
    {
        using var temp = new TempDirectory();
        var (engine, repository, _, _) = LoadRealPackage(temp);
        var rule = repository.Rules.First();

        var result = engine.RevertRule(rule.Id, RuleRepository.ProductName);
        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.EntriesReverted.Should().Be(0);
    }

    [Fact]
    public void SnapshotPrecedesAct_ForMultiActionRule()
    {
        using var temp = new TempDirectory();
        var (engine, repository, _, handler) = LoadRealPackage(temp);
        var product = RuleRepository.ProductName;

        var multiActionRule = repository.Rules.FirstOrDefault(rule => rule.Actions.Length >= 2);
        multiActionRule.Should().NotBeNull("首发包中应存在至少一条多动作规则以验证快照先于执行");

        handler.Calls.Clear();
        var apply = engine.ApplyRule(multiActionRule!.Id, product);
        apply.IsSuccess.Should().BeTrue(apply.Error);

        var n = multiActionRule.Actions.Length;
        // 引擎铁律：先对全部动作快照，再逐项执行 → 前 n 次调用必须全部是 CaptureOriginal。
        handler.Calls.Take(n).Should().OnlyContain(call => call.Method == "CaptureOriginal",
            "任何 Apply 之前必须先完成全部动作的原值快照（Journal 先于动作）");
        handler.Calls.Skip(n).Take(n).Should().OnlyContain(call => call.Method == "Apply");
    }

    [Fact]
    public void RevertAll_WhenNothingApplied_IsNoOpSuccess()
    {
        using var temp = new TempDirectory();
        var (_, repository, _, _) = LoadRealPackage(temp);

        var result = repository.RevertAll();
        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RevertedRules.Should().Be(0);
        result.Value.FullyReverted.Should().BeTrue();
    }
}
