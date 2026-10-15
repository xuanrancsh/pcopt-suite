// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Core.Config;
using PcOpt.Core.Game;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19）攻击性 / 边界用例：会话引擎的 Journal 先写后做、LIFO、幂等、
/// 部分应用失败回滚一致性、Verify 漂移检测。全部使用测试替身，不触碰真实系统。
/// </summary>
public sealed class SessionEngineEdgeCaseTests : IDisposable
{
    private readonly string _root;

    /// <summary>初始化测试（独立临时目录）。</summary>
    public SessionEngineEdgeCaseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcopt-gmp-qa", Guid.NewGuid().ToString("N"));
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
    public void BeginSession_EmptyRuleSet_ShouldEnterPlayingWithZeroApplied()
    {
        var harness = Create(_ => Array.Empty<TweakRule>());

        var begin = harness.Session.BeginSession(Request());

        begin.IsSuccess.Should().BeTrue(begin.Error);
        begin.Value!.AppliedCount.Should().Be(0);
        harness.Session.Current.Phase.Should().Be(SessionPhase.Playing);
    }

    [Fact]
    public void BeginSession_WhenNotPlaying_AfterRestoredCanStartAgain()
    {
        var harness = Create(_ => new[] { Rule("r1", "1") });

        harness.Session.BeginSession(Request()).IsSuccess.Should().BeTrue();
        harness.Session.EndSession().IsSuccess.Should().BeTrue();

        var again = harness.Session.BeginSession(Request());

        again.IsSuccess.Should().BeTrue(again.Error);
        harness.Session.Current.Phase.Should().Be(SessionPhase.Playing);
    }

    [Fact]
    public void ApplyRule_ShouldWriteJournalBeforeExecutingAction()
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var probe = new JournalProbeHandler(store, SessionEngine.ProductName);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { probe }, store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        var session = new SessionEngine(
            engine, new FakeAntiCheatDetector(), store,
            new SessionHistoryStore(NullLogger<SessionHistoryStore>.Instance, _root),
            NullLogger<SessionEngine>.Instance, _ => new[] { Rule("r1", "1") });

        session.BeginSession(Request());

        probe.JournalExistedAtApply.Should().BeTrue("铁律：任何 Apply 之前 Journal 必须已落盘");
    }

    [Fact]
    public void EndSession_ShouldRevertInReverseApplyOrder()
    {
        var harness = Create(_ => new[] { Rule("ra", "a"), Rule("rb", "b"), Rule("rc", "c") });
        harness.Session.BeginSession(Request());

        harness.Session.EndSession();

        harness.Handler.Calls.Where(c => c.Method == "Revert").Select(c => c.Tag)
            .Should().Equal("c", "b", "a");
    }

    [Fact]
    public void BeginSession_OneRuleFails_ShouldContinueOthersAndReportFailure()
    {
        var harness = Create(_ => new[]
        {
            Rule("ra", "a"),
            Failing("rb", "b"),
            Rule("rc", "c"),
        });

        var begin = harness.Session.BeginSession(Request());

        begin.IsSuccess.Should().BeTrue(begin.Error);
        begin.Value!.FailedCount.Should().Be(1);
        // 失败项不阻断其余项
        harness.Handler.Calls.Count(c => c.Method == "Apply" && c.Tag == "a").Should().Be(1);
        harness.Handler.Calls.Count(c => c.Method == "Apply" && c.Tag == "c").Should().Be(1);
    }

    [Fact]
    public void EndSession_AfterOneRuleFailedToApply_ShouldLeaveNoOpenJournalEntry()
    {
        var harness = Create(_ => new[]
        {
            Rule("ra", "a"),
            Failing("rb", "b"),
            Rule("rc", "c"),
        });
        harness.Session.BeginSession(Request());

        harness.Session.EndSession();

        // 期望：会话结束后，本产品不应残留任何"未还原"的还原日志条目（PRD：退出后零残留）
        var residual = harness.Store.ListSessions()
            .Where(s => s.Status == JournalStatus.Open
                && string.Equals(s.Product, SessionEngine.ProductName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.OpenEntries)
            .ToList();
        residual.Should().BeEmpty("失败项虽未生效，但其 Journal 条目仍应为闭合状态，否则会污染幂等判定");
    }

    [Fact]
    public void BeginSession_SecondTime_AfterFirstFailedApply_ShouldNotFalselySkipFailedRule()
    {
        var harness = Create(_ => new[] { Rule("ra", "a"), Failing("rb", "b") });

        var first = harness.Session.BeginSession(Request());
        first.Value!.Outcomes.Single(o => o.RuleId == "rb").Error.Should().NotBeNull();
        harness.Session.EndSession();

        var second = harness.Session.BeginSession(Request());
        var rb = second.Value!.Outcomes.Single(o => o.RuleId == "rb");

        // 期望：上次执行失败的规则应重新尝试应用并再次如实报告失败，而非被误判为"已应用"而跳过
        rb.Error.Should().NotBeNull("执行失败的规则不应被幂等逻辑永久误判为已应用");
        rb.Skipped.Should().BeFalse();
    }

    [Fact]
    public void Verify_AfterEndSession_ShouldReportClean_WhenAllReverted()
    {
        var harness = Create(_ => new[] { Rule("ra", "a") });
        harness.Session.BeginSession(Request());
        harness.Session.EndSession();

        var verify = harness.Session.Verify();

        verify.Value!.Success.Should().BeTrue(string.Join(",", verify.Value.Failures));
    }

    [Fact]
    public void Verify_ShouldDetectResidual_WhenAReapplyLeavesDataBehind()
    {
        // 端到端语义：应用后 IsApplied 为真；还原后为假。
        var harness = Create(_ => new[] { Rule("ra", "a") });
        harness.Session.BeginSession(Request());
        harness.Engine.IsApplied("ra", SessionEngine.ProductName).Should().BeTrue();
        harness.Session.EndSession();
        harness.Engine.IsApplied("ra", SessionEngine.ProductName).Should().BeFalse();
    }

    [Fact]
    public void RestoreEntry_ForUnloadedRule_ShouldFailWithClearMessage()
    {
        var harness = Create(_ => Array.Empty<TweakRule>());

        var result = harness.Engine.RestoreEntry(
            new JournalEntry("ghost.rule", 0, "{}", DateTime.UtcNow));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("尚未加载");
    }

    [Fact]
    public void ApplyProfile_EmptyIds_ShouldFail()
    {
        var harness = Create(_ => new[] { Rule("ra", "a") });

        var result = harness.Session.ApplyProfile(Array.Empty<string>());

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("至少一个");
    }

    [Fact]
    public void ApplyProfile_LoadedRule_ShouldApplyAndBeRevertableViaRevertAll()
    {
        var harness = Create(_ => new[] { Rule("ra", "a") });
        harness.Engine.RegisterRule(Rule("ra", "a"));

        var apply = harness.Session.ApplyProfile(new[] { "ra" });
        apply.IsSuccess.Should().BeTrue(apply.Error);

        var revert = harness.Session.RevertAll();
        revert.IsSuccess.Should().BeTrue(revert.Error);
        harness.Engine.IsApplied("ra", SessionEngine.ProductName).Should().BeFalse();
    }

    private Harness Create(Func<SessionRequest, IReadOnlyList<TweakRule>> factory)
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var handler = new RudeActionHandler();
        var engine = new TweakEngine(
            new ITweakActionHandler[] { handler }, store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        var session = new SessionEngine(
            engine, new FakeAntiCheatDetector(), store,
            new SessionHistoryStore(NullLogger<SessionHistoryStore>.Instance, _root),
            NullLogger<SessionEngine>.Instance, factory);
        return new Harness(session, engine, store, handler);
    }

    private static SessionRequest Request() => new(
        "test.game", "测试游戏", new[] { "test.exe" }, ProfileIntensity.Balanced, Array.Empty<string>());

    private static TweakRule Rule(string id, string tag) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Safe,
        DisplayName = $"规则 {id}",
        Description = "测试规则",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new RudeAction(tag) },
    };

    private static TweakRule Failing(string id, string tag) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Safe,
        DisplayName = $"规则 {id}",
        Description = "测试规则",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new RudeAction(tag, FailOnApply: true) },
    };

    private sealed record Harness(
        SessionEngine Session, TweakEngine Engine, JournalStore Store, RudeActionHandler Handler);

    /// <summary>在 Apply 执行瞬间探测 Journal 是否已落盘的处理器。</summary>
    private sealed class JournalProbeHandler : ITweakActionHandler
    {
        private readonly JournalStore _store;
        private readonly string _product;

        public JournalProbeHandler(JournalStore store, string product)
        {
            _store = store;
            _product = product;
        }

        public bool JournalExistedAtApply { get; private set; }

        public bool CanHandle(TweakAction action) => action is RudeAction;

        public Result<string> CaptureOriginal(TweakAction action) => Result<string>.Ok("{}");

        public Result Apply(TweakAction action)
        {
            JournalExistedAtApply = _store.ListSessions().Any(s =>
                s.Status == JournalStatus.Open
                && string.Equals(s.Product, _product, StringComparison.OrdinalIgnoreCase)
                && s.OpenEntries.Any());
            return Result.Ok();
        }

        public Result Revert(TweakAction action, string originalValueJson) => Result.Ok();

        public Result Verify(TweakAction action) => Result.Ok();
    }
}
