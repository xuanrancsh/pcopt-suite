// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Config;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19）内置规则集：强度档展开 + 合规红线 #9（官方规则只含可自动还原动作）。
/// </summary>
public sealed class RuleSetBuilderTests
{
    [Fact]
    public void Build_Conservative_ShouldContainOnlyPowerAndTimer()
    {
        var rules = RuleSetBuilder.Build(Request(ProfileIntensity.Conservative));

        rules.Select(r => r.Id).Should().Equal(RuleSetBuilder.PowerRuleId, RuleSetBuilder.TimerRuleId);
    }

    [Fact]
    public void Build_Balanced_ShouldAddGamePriority()
    {
        var rules = RuleSetBuilder.Build(Request(ProfileIntensity.Balanced));

        rules.Select(r => r.Id).Should().Contain(RuleSetBuilder.GamePriorityRuleId);
        rules.Should().HaveCount(3);
    }

    [Fact]
    public void Build_Aggressive_ShouldAddBackgroundPriority()
    {
        var rules = RuleSetBuilder.Build(Request(ProfileIntensity.Aggressive));

        rules.Select(r => r.Id).Should().Contain(RuleSetBuilder.BackgroundPriorityRuleId);
        rules.Should().HaveCount(4);
    }

    [Fact]
    public void Build_Balanced_WithoutExecutable_ShouldNotAddPriorityRule()
    {
        var request = new SessionRequest(
            "g", "游戏", Array.Empty<string>(), ProfileIntensity.Balanced, Array.Empty<string>());

        var rules = RuleSetBuilder.Build(request);

        rules.Select(r => r.Id).Should().NotContain(RuleSetBuilder.GamePriorityRuleId);
    }

    [Fact]
    public void DescribeAll_ShouldExposeFourTweaksForGrid()
    {
        RuleSetBuilder.DescribeAll().Should().HaveCount(4);
    }

    [Fact]
    public void AllBuiltRules_ShouldBeAutoRevertible_ComplianceRedline9()
    {
        var all = RuleSetBuilder.DescribeAll()
            .Concat(RuleSetBuilder.Build(Request(ProfileIntensity.Aggressive)))
            .ToList();

        // 产品 A 内置规则集不得含 ShellAction / powercfg 删除等不可还原动作
        var policy = OfficialRulePolicy.ValidatePackage(all);

        policy.IsSuccess.Should().BeTrue(policy.Error);
        all.SelectMany(r => r.Actions).Should().NotContain(a => a is ShellAction);
    }

    [Fact]
    public void PowerRule_ShouldUseSetActive_NotDelete()
    {
        var action = RuleSetBuilder.BuildPowerRule().Actions.Should().ContainSingle()
            .Subject.Should().BeOfType<PowerCfgAction>().Subject;

        action.Kind.Should().Be(PowerActionKind.SetActive);
    }

    [Fact]
    public void BackgroundRule_TargetsExplorer_AsIdle()
    {
        var action = RuleSetBuilder.BuildBackgroundPriorityRule().Actions.Should().ContainSingle()
            .Subject.Should().BeOfType<ProcessPriorityAction>().Subject;

        action.Priority.Should().Be(ProcessPriorityClass.Idle);
    }

    private static SessionRequest Request(ProfileIntensity intensity) => new(
        "g", "游戏", new[] { "game.exe" }, intensity, Array.Empty<string>());
}
