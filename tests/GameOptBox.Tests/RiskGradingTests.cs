// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>风险分级与知识库测试（T22 / T28）。</summary>
public sealed class RiskGradingTests
{
    [Theory]
    [InlineData("Microsoft.WindowsStore", AppxRiskLevel.System)]
    [InlineData("Microsoft.VCLibs.140.00", AppxRiskLevel.System)]
    [InlineData("Microsoft.DesktopAppInstaller", AppxRiskLevel.System)]
    [InlineData("Microsoft.Windows.ShellExperienceHost", AppxRiskLevel.System)]
    [InlineData("Microsoft.MicrosoftSolitaireCollection", AppxRiskLevel.Safe)]
    [InlineData("Microsoft.BingNews", AppxRiskLevel.Safe)]
    [InlineData("Microsoft.XboxGamingOverlay", AppxRiskLevel.Safe)]
    [InlineData("SomeVendor.UnknownApp", AppxRiskLevel.Caution)]
    public void Grade_ClassifiesPackages(string name, AppxRiskLevel expected)
    {
        AppxRiskGrader.Grade(name, null).Should().Be(expected);
    }

    [Fact]
    public void SystemPackages_CannotBeRemoved()
    {
        AppxRiskGrader.CanRemove(AppxRiskLevel.System).Should().BeFalse();
        AppxRiskGrader.CanRemove(AppxRiskLevel.Caution).Should().BeTrue();
        AppxRiskGrader.CanRemove(AppxRiskLevel.Safe).Should().BeTrue();
    }

    [Fact]
    public void Grade_EmptyName_IsCaution()
    {
        AppxRiskGrader.Grade(string.Empty, null).Should().Be(AppxRiskLevel.Caution);
    }

    [Fact]
    public void ServiceKnowledge_DescribesKnownService()
    {
        var knowledge = ServiceKnowledgeBase.Describe("SysMain", "SysMain");
        knowledge.NonEssential.Should().BeTrue();
        knowledge.Recommended.Should().Be(ServiceStartMode.Manual);
        knowledge.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ServiceKnowledge_UnknownService_HasFallbackDescription()
    {
        var knowledge = ServiceKnowledgeBase.Describe("TotallyUnknownServiceXyz", "某服务");
        knowledge.Description.Should().NotBeNullOrWhiteSpace();
        knowledge.NonEssential.Should().BeFalse();
        knowledge.Recommended.Should().Be(ServiceStartMode.Automatic);
    }

    [Fact]
    public void ServiceKnowledge_KnowsSomeServices()
    {
        ServiceKnowledgeBase.KnownCount.Should().BeGreaterThan(20);
        ServiceKnowledgeBase.IsKnown("sysmain").Should().BeTrue();
    }

    [Fact]
    public void ServicePlanner_SelectsOnlyNonEssentialNonOptimal()
    {
        var services = new[]
        {
            new ServiceEntry("SysMain", "SysMain", ServiceStartMode.Automatic, ServiceStartMode.Manual, true, "d"),
            new ServiceEntry("RpcSs", "RPC", ServiceStartMode.Automatic, ServiceStartMode.Automatic, false, "d"),
            new ServiceEntry("DiagTrack", "Diag", ServiceStartMode.Automatic, ServiceStartMode.Disabled, true, "d"),
            new ServiceEntry("WSearch", "Search", ServiceStartMode.Manual, ServiceStartMode.Manual, true, "d"),
        };

        var targets = ServiceOptimizationPlanner.SelectTargets(services);

        targets.Select(target => target.Name).Should().BeEquivalentTo(new[] { "SysMain", "DiagTrack" });
    }

    [Fact]
    public void ServicePlanner_BuildsAutoRevertibleRule()
    {
        var targets = new[]
        {
            new ServiceEntry("SysMain", "SysMain", ServiceStartMode.Automatic, ServiceStartMode.Manual, true, "d"),
        };

        var rule = ServiceOptimizationPlanner.BuildRule(targets);

        rule.Id.Should().Be(ServiceOptimizationPlanner.PlanRuleId);
        rule.Actions.Should().ContainSingle();
        rule.Actions.Should().OnlyContain(action => OfficialRulePolicy.IsAutoRevertible(action));
    }
}
