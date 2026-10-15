// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>内置最小引擎规则定义测试（T36）：不触碰系统的纯定义校验。</summary>
public sealed class MinimalOptimizationEngineTests
{
    [Fact]
    public void BuildRule_HasPowerAndTimerActions()
    {
        var rule = MinimalOptimizationEngine.BuildRule();

        rule.Id.Should().Be(MinimalOptimizationEngine.RuleId);
        rule.Actions.Should().HaveCount(2);

        var power = rule.Actions.OfType<PowerCfgAction>().Single();
        power.Kind.Should().Be(PowerActionKind.SetActive);
        power.Argument.Should().Be(MinimalOptimizationEngine.HighPerformanceScheme.ToString("D"));

        var timer = rule.Actions.OfType<TimerResolutionAction>().Single();
        timer.DesiredResolution100Ns.Should().Be(MinimalOptimizationEngine.TimerResolutionHalfMs);
    }

    [Fact]
    public void BuildRule_IsSafeRisk()
    {
        MinimalOptimizationEngine.BuildRule().Risk.Should().Be(RiskLevel.Safe);
    }

    [Fact]
    public void HighPerformanceScheme_IsWindowsBuiltInGuid()
    {
        MinimalOptimizationEngine.HighPerformanceScheme
            .Should().Be(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
    }
}
