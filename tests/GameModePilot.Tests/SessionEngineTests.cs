// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Config;
using PcOpt.Core.Game;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// <see cref="SessionEngine"/> 单元测试（T13）：Begin / Apply / Verify / Revert 全链路 + IFEO 降级分支。
/// 全部使用测试替身动作，不触碰真实系统（注册表 / 电源计划 / 进程），Journal 落在临时目录。
/// </summary>
public sealed class SessionEngineTests : IDisposable
{
    private readonly string _rootDirectory;

    /// <summary>初始化测试（独立临时目录）。</summary>
    public SessionEngineTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "pcopt-gmp-tests", Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论
        }
    }

    [Fact]
    public void BeginSession_ShouldApplyAllRules_AndEnterPlaying()
    {
        var (session, _, handler) = Create(_ => new[] { Rule("rule.a", "a"), Rule("rule.b", "b") });

        var begin = session.BeginSession(Request());

        begin.IsSuccess.Should().BeTrue(begin.Error);
        begin.Value!.AppliedCount.Should().Be(2);
        begin.Value.FailedCount.Should().Be(0);
        session.Current.Phase.Should().Be(SessionPhase.Playing);
        handler.Calls.Count(call => call.Method == "Apply").Should().Be(2);
    }

    [Fact]
    public void EndSession_ShouldRevertAllRulesLifo_AndEnterRestored()
    {
        var (session, engine, handler) = Create(_ => new[] { Rule("rule.a", "a"), Rule("rule.b", "b") });
        session.BeginSession(Request());

        var end = session.EndSession();

        end.IsSuccess.Should().BeTrue(end.Error);
        end.Value!.FullyReverted.Should().BeTrue();
        session.Current.Phase.Should().Be(SessionPhase.Restored);
        engine.IsApplied("rule.a", SessionEngine.ProductName).Should().BeFalse();
        engine.IsApplied("rule.b", SessionEngine.ProductName).Should().BeFalse();
        handler.Calls.Count(call => call.Method == "Revert").Should().Be(2);

        // LIFO：后应用的规则先还原
        handler.Calls.Where(call => call.Method == "Revert")
            .Select(call => ((FakeAction)call.Action).Tag)
            .Should().Equal("b", "a");
    }

    [Fact]
    public void Verify_ShouldReportResidualWhilePlaying_AndCleanAfterRevert()
    {
        var (session, _, _) = Create(_ => new[] { Rule("rule.a", "a") });
        session.BeginSession(Request());

        // Result.IsSuccess 表示"校验操作本身成功"；会话是否无残留由 SessionVerification.Success 表达
        var before = session.Verify();
        before.IsSuccess.Should().BeTrue(before.Error);
        before.Value!.Success.Should().BeFalse();
        before.Value.Failures.Should().Contain("rule.a");

        session.EndSession();

        var after = session.Verify();
        after.Value!.Success.Should().BeTrue(string.Join(",", after.Value.Failures));
        after.Value.Failures.Should().BeEmpty();
    }

    [Fact]
    public void BeginSession_WhenAlreadyPlaying_ShouldFail()
    {
        var (session, _, _) = Create(_ => new[] { Rule("rule.a", "a") });
        session.BeginSession(Request()).IsSuccess.Should().BeTrue();

        var second = session.BeginSession(Request());

        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("进行中");
    }

    [Fact]
    public void BeginSession_DisabledRuleIds_ShouldBeSkipped()
    {
        var (session, _, handler) = Create(_ => new[] { Rule("rule.a", "a"), Rule("rule.b", "b") });

        var begin = session.BeginSession(Request("rule.b"));

        begin.Value!.AppliedCount.Should().Be(1);
        handler.Calls.Count(call => call.Method == "Apply" && ((FakeAction)call.Action).Tag == "a").Should().Be(1);
        handler.Calls.Count(call => call.Method == "Apply" && ((FakeAction)call.Action).Tag == "b").Should().Be(0);
    }

    [Fact]
    public void BeginSession_WhenKernelAntiCheat_ShouldUseIfeoFallback()
    {
        var registry = new RecordingRegistryHandler();
        var detector = new FakeAntiCheatDetector(new AntiCheatDetectionResult(
            AntiCheatProfile.Kernel,
            KnownAntiCheat.Vanguard,
            new[] { AntiCheatFeature.Driver("vgk.sys") }));

        var (session, engine, _) = Create(
            _ => new[] { PriorityRule("game.exe.priority", "VALORANT.exe", ProcessPriorityClass.High) },
            detector,
            new ITweakActionHandler[] { registry });

        var begin = session.BeginSession(Request());

        begin.IsSuccess.Should().BeTrue(begin.Error);
        begin.Value!.UsedIfeoFallback.Should().BeTrue();
        begin.Value.Outcomes[0].UsedIfeoFallback.Should().BeTrue();

        registry.LastAction.Should().NotBeNull();
        registry.LastAction!.Path.Should().Be(IfeoPaths.PerfOptions("VALORANT.exe"));
        registry.LastAction.Name.Should().Be(IfeoFallbackPlanner.CpuPriorityClassValueName);
        registry.LastAction.Value.Should().Be(IfeoPaths.CpuPriorityClassValues.High);

        // 原规则未被应用；降级规则 Id 带 .ifeo 后缀进入 Journal
        engine.IsApplied("game.exe.priority", SessionEngine.ProductName).Should().BeFalse();
        engine.IsApplied("game.exe.priority.ifeo", SessionEngine.ProductName).Should().BeTrue();

        var end = session.EndSession();
        end.IsSuccess.Should().BeTrue(end.Error);
        engine.IsApplied("game.exe.priority.ifeo", SessionEngine.ProductName).Should().BeFalse();
    }

    [Fact]
    public void BeginSession_WhenNoKernelAntiCheat_ShouldNotUseIfeo()
    {
        var registry = new RecordingRegistryHandler();
        var processHandler = new RecordingProcessHandler();

        var (session, engine, _) = Create(
            _ => new[] { PriorityRule("game.exe.priority", "cs2.exe", ProcessPriorityClass.High) },
            new FakeAntiCheatDetector(),
            new ITweakActionHandler[] { processHandler, registry });

        var begin = session.BeginSession(Request());

        begin.Value!.UsedIfeoFallback.Should().BeFalse();
        processHandler.Actions.Should().ContainSingle();
        registry.Actions.Should().BeEmpty();
        engine.IsApplied("game.exe.priority", SessionEngine.ProductName).Should().BeTrue();
    }

    [Fact]
    public void ApplyProfile_UnknownRule_ShouldReportFailureOutcome()
    {
        var (session, _, _) = Create(_ => new[] { Rule("rule.a", "a") });

        var result = session.ApplyProfile(new[] { "missing.rule" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Outcomes[0].Error.Should().NotBeNull();
        result.Value.Outcomes[0].Applied.Should().BeFalse();
    }

    [Fact]
    public void BeginSession_ShouldRecordSessionHistory()
    {
        var history = new SessionHistoryStore(NullLogger<SessionHistoryStore>.Instance, _rootDirectory);
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _rootDirectory);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new FakeActionHandler() },
            store,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance);
        var session = new SessionEngine(
            engine,
            new FakeAntiCheatDetector(),
            store,
            history,
            NullLogger<SessionEngine>.Instance,
            _ => new[] { Rule("rule.a", "a") });

        session.BeginSession(Request());
        session.EndSession();

        var records = history.ListRecent(10);
        records.Should().ContainSingle();
        records[0].GameName.Should().Be("测试游戏");
        records[0].FullyReverted.Should().BeTrue();
        records[0].EndedAtUtc.Should().NotBeNull();
    }

    private (SessionEngine Session, TweakEngine Engine, FakeActionHandler Handler) Create(
        Func<SessionRequest, IReadOnlyList<TweakRule>> factory,
        IAntiCheatDetector? detector = null,
        ITweakActionHandler[]? extraHandlers = null)
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, _rootDirectory);
        var handler = new FakeActionHandler();
        var handlers = new List<ITweakActionHandler> { handler };
        if (extraHandlers is not null)
        {
            handlers.AddRange(extraHandlers);
        }

        var engine = new TweakEngine(handlers, store, new TestSystemEnvironment(), NullLogger<TweakEngine>.Instance);
        var history = new SessionHistoryStore(NullLogger<SessionHistoryStore>.Instance, _rootDirectory);
        var session = new SessionEngine(
            engine,
            detector ?? new FakeAntiCheatDetector(),
            store,
            history,
            NullLogger<SessionEngine>.Instance,
            factory);

        return (session, engine, handler);
    }

    private static SessionRequest Request(params string[] disabledRuleIds) => new(
        "test.game",
        "测试游戏",
        new[] { "test.exe" },
        ProfileIntensity.Balanced,
        disabledRuleIds);

    private static TweakRule Rule(string id, string tag) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Safe,
        DisplayName = $"规则 {id}",
        Description = "测试规则",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new FakeAction(tag) },
    };

    private static TweakRule PriorityRule(string id, string executable, ProcessPriorityClass priority) => new()
    {
        Id = id,
        Category = "游戏",
        Risk = RiskLevel.Notice,
        DisplayName = $"进程优先级 {id}",
        Description = "测试规则",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new ProcessPriorityAction(executable, priority, null) },
    };
}
