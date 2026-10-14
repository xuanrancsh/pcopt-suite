// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Platform;
using PcOpt.Core.Tests.TestDoubles;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.Tweak;

/// <summary>
/// <see cref="TweakEngine"/> 单元测试：验签拒绝、条件匹配、幂等、先 Journal 后执行、LIFO 还原（架构 3.1 / 8.4）。
/// </summary>
public sealed class TweakEngineTests : IDisposable
{
    private const string RuleId = "test.fake.apply";
    private const string Product = "TestProduct";

    private const string SignedRuleYaml = """
        id: reg.test.signed
        category: 测试
        risk: safe
        displayName: 签名规则测试
        actions:
          - type: RegistrySet
            path: HKCU\Software\pcopt-test
            name: Signed
            value: 1
            valueKind: DWord
        """;

    private readonly string _rootDirectory;
    private readonly List<string> _trackedDirectories = new();

    public TweakEngineTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "pcopt-engine-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        foreach (var directory in _trackedDirectories.Append(_rootDirectory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ---------- 签名加载 ----------

    [Fact]
    public void LoadRulePackage_Unsigned_ShouldBeRejected_AndRuleNotRegistered()
    {
        // 引擎已配置验签器：加载"无签名"规则包必须被拒绝并给出明确错误
        var (_, publicKey, _) = TestKeys.CreateAndSign(Encoding.UTF8.GetBytes(SignedRuleYaml));
        var (engine, _, _) = CreateEngine(verifier: RuleSignatureVerifier.Create(publicKey).Value!);
        var content = Encoding.UTF8.GetBytes(SignedRuleYaml);

        var result = engine.LoadRulePackage(content, Array.Empty<byte>());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未签名");

        // 拒绝加载后规则不可用
        var apply = engine.ApplyRule("reg.test.signed", Product);
        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("尚未加载");
    }

    [Fact]
    public void LoadRulePackage_Tampered_ShouldBeRejected_AndRuleNotRegistered()
    {
        var (privateKey, publicKey, _) = TestKeys.CreateAndSign(Encoding.UTF8.GetBytes(SignedRuleYaml));
        var (engine, _, _) = CreateEngine(verifier: RuleSignatureVerifier.Create(publicKey).Value!);

        var tampered = Encoding.UTF8.GetBytes(SignedRuleYaml.Replace("value: 1", "value: 999"));
        var signature = TestKeys.Sign(privateKey, Encoding.UTF8.GetBytes(SignedRuleYaml));

        var result = engine.LoadRulePackage(tampered, signature);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("篡改");

        var apply = engine.ApplyRule("reg.test.signed", Product);
        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("尚未加载");
    }

    [Fact]
    public void LoadRulePackage_ValidSignature_ShouldLoadAndApply()
    {
        var content = Encoding.UTF8.GetBytes(SignedRuleYaml);
        var (privateKey, publicKey, _) = TestKeys.CreateAndSign(content);
        var signature = TestKeys.Sign(privateKey, content);

        // 签名规则包含真实 RegistrySet 动作：注册真实 handler，写入位置为 HKCU 专用测试键
        // （HKCU\Software\pcopt-test，与 RegistrySetActionHandlerTests 同一约定），
        // 测试末尾还原，保证不残留注册表改动。
        var store = CreateStore();
        var registryHandler = new PcOpt.Core.Tweak.Handlers.RegistrySetActionHandler(
            NullLogger<PcOpt.Core.Tweak.Handlers.RegistrySetActionHandler>.Instance);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { registryHandler },
            store,
            TestSystemEnvironment.Win11X64(),
            NullLogger<TweakEngine>.Instance,
            RuleSignatureVerifier.Create(publicKey).Value!);

        var load = engine.LoadRulePackage(content, signature);
        load.IsSuccess.Should().BeTrue(load.Error);
        load.Value!.Id.Should().Be("reg.test.signed");

        var apply = engine.ApplyRule("reg.test.signed", Product);
        apply.IsSuccess.Should().BeTrue(apply.Error);
        apply.Value!.WasApplied.Should().BeTrue();

        var sessions = store.ListSessions();
        sessions.Should().HaveCount(1);
        sessions[0].Entries.Should().HaveCount(1);

        // 还原：删除测试键写入的值，保持机器干净（"还原优先"同时是测试卫生）
        var revert = engine.RevertRule("reg.test.signed", Product);
        revert.IsSuccess.Should().BeTrue(revert.Error);
        revert.Value!.EntriesReverted.Should().Be(1);
    }

    [Fact]
    public void LoadRulePackage_WithoutVerifier_ShouldBeRejected()
    {
        var (engine, _, _) = CreateEngine();
        var content = Encoding.UTF8.GetBytes(SignedRuleYaml);

        var result = engine.LoadRulePackage(content, new byte[64]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未配置签名公钥");
    }

    [Fact]
    public void LoadRuleFromYaml_Unsigned_ShouldParseAndRegister()
    {
        var (engine, _, _) = CreateEngine();

        var load = engine.LoadRuleFromYaml(SignedRuleYaml);

        load.IsSuccess.Should().BeTrue(load.Error);
        engine.LoadedRules.Should().HaveCount(1);
    }

    // ---------- 条件匹配 ----------

    [Fact]
    public void ApplyRule_ConditionMismatch_ShouldFail_WithoutTouchingSystem()
    {
        var (engine, store, handler) = CreateEngine();
        engine.RegisterRule(BuildRule(conditions: new[] { new OsCondition { MinBuild = 26000 } }));

        var apply = engine.ApplyRule(RuleId, Product);

        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("不适用");
        handler.Calls.Should().BeEmpty();
        store.ListSessions().Should().BeEmpty();
    }

    // ---------- 幂等 ----------

    [Fact]
    public void ApplyRule_Twice_ShouldBeIdempotent()
    {
        var (engine, _, handler) = CreateEngine();
        engine.RegisterRule(BuildRule());

        var first = engine.ApplyRule(RuleId, Product);
        var second = engine.ApplyRule(RuleId, Product);

        first.IsSuccess.Should().BeTrue();
        first.Value!.WasApplied.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value!.AlreadyApplied.Should().BeTrue();
        second.Value!.WasApplied.Should().BeFalse();

        // 第二次未触发任何执行
        handler.Calls.Count(c => c.Method == "Apply").Should().Be(2);
    }

    [Fact]
    public void IsApplied_ShouldTrackJournalState()
    {
        var (engine, _, _) = CreateEngine();
        engine.RegisterRule(BuildRule());

        engine.IsApplied(RuleId, Product).Should().BeFalse();
        engine.ApplyRule(RuleId, Product);
        engine.IsApplied(RuleId, Product).Should().BeTrue();
    }

    // ---------- 先 Journal 后执行 ----------

    [Fact]
    public void ApplyRule_ShouldWriteJournalBeforeExecutingAnyAction()
    {
        var (engine, store, handler) = CreateEngine();
        engine.RegisterRule(BuildRule());

        handler.ApplyHandler = _ =>
        {
            // 执行时刻：Journal 必须已原子落盘且包含全部条目
            var sessions = store.ListSessions();
            sessions.Should().HaveCount(1);
            sessions[0].Entries.Should().HaveCount(2);
            sessions[0].Entries.Should().OnlyContain(e => e.RuleId == RuleId);
            return Result.Ok();
        };

        var apply = engine.ApplyRule(RuleId, Product);

        apply.IsSuccess.Should().BeTrue(apply.Error);
    }

    [Fact]
    public void ApplyRule_SnapshotFailure_ShouldNotExecute_AndNotCreateJournal()
    {
        var (engine, store, handler) = CreateEngine();
        engine.RegisterRule(BuildRule());
        handler.CaptureOriginalHandler = action =>
            ((FakeAction)action).Tag == "action-1"
                ? Result<string>.Fail("模拟快照失败：无法读取原值")
                : Result<string>.Ok("{}");

        var apply = engine.ApplyRule(RuleId, Product);

        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("快照失败");
        handler.Calls.Where(c => c.Method == "Apply").Should().BeEmpty();
        store.ListSessions().Should().BeEmpty();
    }

    [Fact]
    public void ApplyRule_ExecutionFailure_ShouldPreserveJournal_ForSafeRevert()
    {
        var (engine, store, handler) = CreateEngine();
        engine.RegisterRule(BuildRule());
        var callCount = 0;
        handler.ApplyHandler = _ =>
        {
            callCount++;
            return callCount == 1 ? Result.Ok() : Result.Fail("模拟执行失败");
        };

        var apply = engine.ApplyRule(RuleId, Product);

        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("动作 1 执行失败");

        // Journal 完整保留：系统可恢复
        var sessions = store.ListSessions();
        sessions.Should().HaveCount(1);
        sessions[0].Status.Should().Be(JournalStatus.Open);
        sessions[0].Entries.Should().HaveCount(2);

        var revert = engine.RevertRule(RuleId, Product);
        revert.IsSuccess.Should().BeTrue(revert.Error);
        revert.Value!.EntriesReverted.Should().Be(2);
    }

    [Fact]
    public void ApplyRule_UnknownHandler_ShouldFail()
    {
        var store = CreateStore();
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            store,
            TestSystemEnvironment.Win11X64(),
            NullLogger<TweakEngine>.Instance);
        engine.RegisterRule(BuildRule());

        var apply = engine.ApplyRule(RuleId, Product);

        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("没有注册的处理器");
    }

    [Fact]
    public void Constructor_DuplicateHandler_ShouldThrow()
    {
        var store = CreateStore();

        var act = () => new TweakEngine(
            new ITweakActionHandler[] { new FakeActionHandler(typeof(FakeAction)), new FakeActionHandler(typeof(FakeAction)) },
            store,
            TestSystemEnvironment.Win11X64(),
            NullLogger<TweakEngine>.Instance);

        act.Should().Throw<ArgumentException>();
    }

    // ---------- 还原 ----------

    [Fact]
    public void RevertRule_ShouldRestoreInLifoOrder_AndCloseSession()
    {
        var (engine, store, handler) = CreateEngine();
        engine.RegisterRule(BuildRule());
        engine.ApplyRule(RuleId, Product);

        var revert = engine.RevertRule(RuleId, Product);

        revert.IsSuccess.Should().BeTrue(revert.Error);
        revert.Value!.EntriesReverted.Should().Be(2);
        revert.Value!.FullyReverted.Should().BeTrue();

        // LIFO：动作序号大的先还原
        handler.Calls.Where(c => c.Method == "Revert").Select(c => ((FakeAction)c.Action).Tag)
            .Should().Equal("action-1", "action-0");

        var session = store.ListSessions()[0];
        session.Status.Should().Be(JournalStatus.Closed);
        session.Entries.Should().OnlyContain(e => e.RevertedAt != null);
    }

    [Fact]
    public void RevertRule_WithoutApply_ShouldReturnZero()
    {
        var (engine, _, _) = CreateEngine();
        engine.RegisterRule(BuildRule());

        var revert = engine.RevertRule(RuleId, Product);

        revert.IsSuccess.Should().BeTrue();
        revert.Value!.EntriesReverted.Should().Be(0);
        revert.Value!.FullyReverted.Should().BeTrue();
    }

    [Fact]
    public void RevertRule_ShouldBeScopedToProduct()
    {
        var (engine, _, handler) = CreateEngine();
        engine.RegisterRule(BuildRule());
        engine.ApplyRule(RuleId, "ProductA");

        var wrongProduct = engine.RevertRule(RuleId, "ProductB");
        wrongProduct.Value!.EntriesReverted.Should().Be(0);
        handler.Calls.Count(c => c.Method == "Revert").Should().Be(0);

        var rightProduct = engine.RevertRule(RuleId, "ProductA");
        rightProduct.Value!.EntriesReverted.Should().Be(2);
    }

    [Fact]
    public void ApplyRule_BlankInputs_ShouldFail()
    {
        var (engine, _, _) = CreateEngine();
        engine.RegisterRule(BuildRule());

        engine.ApplyRule("", Product).IsSuccess.Should().BeFalse();
        engine.ApplyRule(RuleId, "").IsSuccess.Should().BeFalse();
        engine.RevertRule("", Product).IsSuccess.Should().BeFalse();
        engine.RevertRule(RuleId, "").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ApplyRule_UnloadedRule_ShouldFail()
    {
        var (engine, _, _) = CreateEngine();

        var apply = engine.ApplyRule("nope.missing.rule", Product);

        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("尚未加载");
    }

    // ---------- 辅助 ----------

    private static TweakRule BuildRule(string ruleId = RuleId, OsCondition[]? conditions = null, int actionCount = 2)
    {
        var actions = Enumerable.Range(0, actionCount)
            .Select(i => (TweakAction)new FakeAction($"action-{i}"))
            .ToArray();
        return new TweakRule
        {
            Id = ruleId,
            Category = "测试",
            Risk = RiskLevel.Safe,
            DisplayName = "测试规则",
            Description = "引擎单元测试",
            ConsequenceHints = Array.Empty<string>(),
            Conditions = conditions ?? Array.Empty<OsCondition>(),
            Actions = actions,
        };
    }

    private (TweakEngine Engine, JournalStore Store, FakeActionHandler Handler) CreateEngine(
        ISystemEnvironment? environment = null,
        RuleSignatureVerifier? verifier = null)
    {
        var store = CreateStore();
        var handler = new FakeActionHandler(typeof(FakeAction));
        var engine = new TweakEngine(
            new ITweakActionHandler[] { handler },
            store,
            environment ?? TestSystemEnvironment.Win11X64(),
            NullLogger<TweakEngine>.Instance,
            verifier);
        return (engine, store, handler);
    }

    private JournalStore CreateStore()
    {
        var root = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        _trackedDirectories.Add(root);
        return new JournalStore(NullLogger<JournalStore>.Instance, root);
    }
}
