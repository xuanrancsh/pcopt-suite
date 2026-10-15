// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Core.Journal;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>优化-复测闭环"应用"环节测试（T36 / PRD C-P0-5）：优先 A，缺失时降级到内置最小引擎。</summary>
public sealed class OptimizationLoopServiceTests
{
    private static Diagnosis Diagnosis(string recommendationId, params string[] ruleIds) => new(
        "symptom.test",
        "测试症状",
        new[] { "测试证据" },
        recommendationId,
        "测试建议",
        ruleIds,
        "测试收益",
        DiagnosisSeverity.Notice);

    private static (OptimizationLoopService Loop, FakeGameModePilotClient Pilot, FakeMemoryCleaner Memory, string Root) Build()
    {
        var root = Path.Combine(Path.GetTempPath(), "framelab-journal-" + Guid.NewGuid().ToString("N"));
        var journal = new JournalStore(NullLogger<JournalStore>.Instance, root);
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new FakeTweakActionHandler() },
            journal,
            new WindowsSystemEnvironment(),
            NullLogger<TweakEngine>.Instance);

        var memory = new FakeMemoryCleaner();
        var minimal = new MinimalOptimizationEngine(engine, memory, NullLogger<MinimalOptimizationEngine>.Instance);
        var pilot = new FakeGameModePilotClient();
        var loop = new OptimizationLoopService(pilot, minimal, NullLogger<OptimizationLoopService>.Instance);
        return (loop, pilot, memory, root);
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
    public void Apply_EmptyDiagnoses_Fails()
    {
        var (loop, _, _, root) = Build();
        try
        {
            loop.Apply(Array.Empty<Diagnosis>()).IsSuccess.Should().BeFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Apply_WhenPilotUnavailable_AndOnlyPilotAdvice_FailsWithInstallPrompt()
    {
        var (loop, pilot, memory, root) = Build();
        try
        {
            pilot.Running = false;
            var result = loop.Apply(new[] { Diagnosis(RecommendationIds.InterruptAffinity, "latency.interrupt.affinity") });

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().Contain("游戏模式");
            memory.PurgeCount.Should().Be(0); // 最小引擎未被调用（该建议只有 A 能处理）
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Apply_WhenPilotRunning_UsesPilotChannel()
    {
        var (loop, pilot, memory, root) = Build();
        try
        {
            pilot.Running = true;
            var result = loop.Apply(new[] { Diagnosis(RecommendationIds.InterruptAffinity, "latency.interrupt.affinity") });

            result.IsSuccess.Should().BeTrue();
            result.Value!.Channel.Should().Be("GameModePilot");
            pilot.LastRuleIds.Should().Contain("latency.interrupt.affinity");
            memory.PurgeCount.Should().Be(0);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Apply_WhenPilotUnavailable_AndMinimalAdvice_UsesBuiltInEngine()
    {
        var (loop, pilot, memory, root) = Build();
        try
        {
            pilot.Running = false;
            var result = loop.Apply(new[]
            {
                Diagnosis(RecommendationIds.PowerPlan, "power.highperf.activate"),
                Diagnosis(RecommendationIds.MemoryCleanup, "mem.standby.purge"),
            });

            result.IsSuccess.Should().BeTrue();
            result.Value!.Channel.Should().Be("内置最小引擎");
            memory.PurgeCount.Should().Be(1);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Apply_WhenPilotCallFails_FallsBackToBuiltInEngine()
    {
        var (loop, pilot, memory, root) = Build();
        try
        {
            pilot.Running = true;
            pilot.ApplyResult = Result<string>.Fail("引擎拒绝");
            var result = loop.Apply(new[] { Diagnosis(RecommendationIds.PowerPlan, "power.highperf.activate") });

            result.IsSuccess.Should().BeTrue();
            result.Value!.Channel.Should().Be("内置最小引擎");
            memory.PurgeCount.Should().Be(1);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Revert_WhenPilotUnavailable_RevertsBuiltInEngine()
    {
        var (loop, pilot, _, root) = Build();
        try
        {
            pilot.Running = false;
            var result = loop.Revert();
            // 未应用过 → 内核 RevertRule 允许"无可还原"（不抛异常）
            result.Should().NotBeNull();
        }
        finally
        {
            Cleanup(root);
        }
    }
}
