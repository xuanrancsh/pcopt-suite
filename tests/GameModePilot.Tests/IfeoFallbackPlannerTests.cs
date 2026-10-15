// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// <see cref="IfeoFallbackPlanner"/> 单元测试（T13 / PRD A-P0-6）：内核反作弊命中时进程调度降级 IFEO。
/// </summary>
public sealed class IfeoFallbackPlannerTests
{
    [Fact]
    public void ContainsProcessSchedulingAction_ProcessRule_ShouldBeTrue()
    {
        IfeoFallbackPlanner.ContainsProcessSchedulingAction(ProcessRule()).Should().BeTrue();
    }

    [Fact]
    public void ContainsProcessSchedulingAction_NonProcessRule_ShouldBeFalse()
    {
        IfeoFallbackPlanner.ContainsProcessSchedulingAction(PlainRule()).Should().BeFalse();
    }

    [Theory]
    [InlineData(ProcessPriorityClass.Idle, IfeoPaths.CpuPriorityClassValues.Idle)]
    [InlineData(ProcessPriorityClass.BelowNormal, IfeoPaths.CpuPriorityClassValues.BelowNormal)]
    [InlineData(ProcessPriorityClass.Normal, IfeoPaths.CpuPriorityClassValues.Normal)]
    [InlineData(ProcessPriorityClass.AboveNormal, IfeoPaths.CpuPriorityClassValues.AboveNormal)]
    [InlineData(ProcessPriorityClass.High, IfeoPaths.CpuPriorityClassValues.High)]
    [InlineData(ProcessPriorityClass.Realtime, IfeoPaths.CpuPriorityClassValues.Realtime)]
    public void MapCpuPriorityClass_ShouldMapToWin32Code(ProcessPriorityClass priority, int expected)
    {
        IfeoFallbackPlanner.MapCpuPriorityClass(priority).Should().Be(expected);
    }

    [Fact]
    public void BuildIfeoRule_ShouldReplaceActionWithRegistrysetOnPerfOptions()
    {
        var built = IfeoFallbackPlanner.BuildIfeoRule(ProcessRule());

        built.Id.Should().Be("game.exe.priority" + IfeoFallbackPlanner.IfeoSuffix);
        var action = built.Actions.Should().ContainSingle().Subject.Should().BeOfType<RegistrySetAction>().Subject;
        action.Path.Should().Be(IfeoPaths.PerfOptions("VALORANT.exe"));
        action.Name.Should().Be(IfeoFallbackPlanner.CpuPriorityClassValueName);
        action.Value.Should().Be(IfeoPaths.CpuPriorityClassValues.High);
    }

    [Fact]
    public void BuildIfeoRule_NonProcessRule_ShouldReturnSameInstance()
    {
        var rule = PlainRule();

        var built = IfeoFallbackPlanner.BuildIfeoRule(rule);

        built.Should().BeSameAs(rule);
    }

    private static TweakRule ProcessRule() => new()
    {
        Id = "game.exe.priority",
        Category = "游戏",
        Risk = RiskLevel.Notice,
        DisplayName = "给游戏进程更高的调度优先级",
        Description = "测试",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new ProcessPriorityAction("D:\\Games\\VALORANT.exe", ProcessPriorityClass.High, null) },
    };

    private static TweakRule PlainRule() => new()
    {
        Id = "game.timer.resolution",
        Category = "游戏",
        Risk = RiskLevel.Notice,
        DisplayName = "计时器",
        Description = "测试",
        ConsequenceHints = Array.Empty<string>(),
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new TimerResolutionAction(5000) },
    };
}
