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
/// 独立验收（T19 · 第 2 轮）缺陷 A 证伪式复查：失败规则在会话结束后不得残留 Open 条目、
/// 不得污染幂等判定（失败项须在下次会话重试）。覆盖：单条失败 / 多条失败 / 全部失败 / 失败后再成功。
/// </summary>
public sealed class SessionEngineFailureRollbackTests : IDisposable
{
    private readonly string _root;

    /// <summary>初始化测试（独立临时目录）。</summary>
    public SessionEngineFailureRollbackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcopt-gmp-qa2", Guid.NewGuid().ToString("N"));
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
    public void MultipleRulesFail_EndSession_ShouldLeaveNoOpenJournalEntries()
    {
        var handler = new ToggleHandler { FailingTags = { "b", "c" } };
        var harness = Create(handler, _ => new[] { Rule("ra", "a"), Rule("rb", "b"), Rule("rc", "c") });

        harness.Session.BeginSession(Request());
        var end = harness.Session.EndSession();

        end.IsSuccess.Should().BeTrue(end.Error);
        OpenResidual(harness).Should().BeEmpty("全部失败项也须在会话结束时闭合");
    }

    [Fact]
    public void AllRulesFail_EndSession_ShouldLeaveNoOpenJournalEntries()
    {
        var handler = new ToggleHandler { FailingTags = { "a", "b" } };
        var harness = Create(handler, _ => new[] { Rule("ra", "a"), Rule("rb", "b") });

        harness.Session.BeginSession(Request());
        harness.Session.EndSession();

        OpenResidual(harness).Should().BeEmpty();
    }

    [Fact]
    public void FailedRule_SecondSession_ShouldBeRetried_NotFalselySkipped()
    {
        var handler = new ToggleHandler { FailingTags = { "b" } };
        var harness = Create(handler, _ => new[] { Rule("ra", "a"), Rule("rb", "b") });

        harness.Session.BeginSession(Request());
        harness.Session.EndSession();

        // 第二会话：rb 上次执行失败，须重新尝试（再次如实报失败），不得被误判为"已应用"跳过
        var second = harness.Session.BeginSession(Request());
        var rb = second.Value!.Outcomes.Single(o => o.RuleId == "rb");

        rb.Skipped.Should().BeFalse("上次失败的规则不应被永久误判为已应用");
        rb.Error.Should().NotBeNull("第二次仍处于失败态，应如实报告失败");
    }

    [Fact]
    public void FailThenSucceed_SecondSession_ShouldActuallyApply()
    {
        var handler = new ToggleHandler { FailingTags = { "a" } };
        var harness = Create(handler, _ => new[] { Rule("ra", "a") });

        var first = harness.Session.BeginSession(Request());
        first.Value!.Outcomes.Single(o => o.RuleId == "ra").Error.Should().NotBeNull("首次应用应如实报告失败");
        // 失败项在会话存续期间其 Journal 条目存在（先写后做，属预期）；关键在于会话结束后必须闭合
        harness.Session.EndSession();
        OpenResidual(harness).Should().BeEmpty();
        harness.Engine.IsApplied("ra", SessionEngine.ProductName).Should().BeFalse("会话结束后失败项须已闭合，不得残留");

        // 第二次：处理器恢复正常 → 规则应真实应用
        handler.FailingTags.Clear();
        var second = harness.Session.BeginSession(Request());
        var ra = second.Value!.Outcomes.Single(o => o.RuleId == "ra");

        ra.Applied.Should().BeTrue("上次失败后本次应重新执行并成功");
        harness.Engine.IsApplied("ra", SessionEngine.ProductName).Should().BeTrue();

        harness.Session.EndSession();
        OpenResidual(harness).Should().BeEmpty();
    }

    [Fact]
    public void EndSession_AfterFailures_ShouldReportFullyReverted()
    {
        var handler = new ToggleHandler { FailingTags = { "b" } };
        var harness = Create(handler, _ => new[] { Rule("ra", "a"), Rule("rb", "b") });

        harness.Session.BeginSession(Request());
        var end = harness.Session.EndSession();

        end.Value!.FullyReverted.Should().BeTrue("失败项经兜底闭合后整体应视为已完全还原");
        harness.Session.Verify().Value!.Success.Should().BeTrue();
    }

    private static List<JournalEntry> OpenResidual(Harness harness) =>
        harness.Store.ListSessions()
            .Where(s => s.Status == JournalStatus.Open
                && string.Equals(s.Product, SessionEngine.ProductName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.OpenEntries)
            .ToList();

    private Harness Create(ToggleHandler handler, Func<SessionRequest, IReadOnlyList<TweakRule>> factory)
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _root);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { handler }, store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        var session = new SessionEngine(
            engine, new FakeAntiCheatDetector(), store,
            new SessionHistoryStore(NullLogger<SessionHistoryStore>.Instance, _root),
            NullLogger<SessionEngine>.Instance, factory);
        return new Harness(session, engine, store);
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
        Actions = new TweakAction[] { new ToggleAction(tag) },
    };

    private sealed record Harness(SessionEngine Session, TweakEngine Engine, JournalStore Store);

    /// <summary>可由处理器按标识控制成败的动作。</summary>
    private sealed record ToggleAction(string Tag) : TweakAction;

    /// <summary>可控成败的处理器：<see cref="FailingTags"/> 内的动作 Apply 返回失败。</summary>
    private sealed class ToggleHandler : ITweakActionHandler
    {
        /// <summary>当前失败的标识集合。</summary>
        public HashSet<string> FailingTags { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public bool CanHandle(TweakAction action) => action is ToggleAction;

        /// <inheritdoc/>
        public Result<string> CaptureOriginal(TweakAction action) => Result<string>.Ok("{}");

        /// <inheritdoc/>
        public Result Apply(TweakAction action) =>
            FailingTags.Contains(((ToggleAction)action).Tag) ? Result.Fail("模拟执行失败") : Result.Ok();

        /// <inheritdoc/>
        public Result Revert(TweakAction action, string originalValueJson) => Result.Ok();

        /// <inheritdoc/>
        public Result Verify(TweakAction action) => Result.Ok();
    }
}
