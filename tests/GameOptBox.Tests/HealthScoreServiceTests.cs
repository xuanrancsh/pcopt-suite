// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>健康分算法测试（T20，PRD B-P0-10：算法文档化）。</summary>
public sealed class HealthScoreServiceTests
{
    private readonly HealthScoreService _service = new();

    [Fact]
    public void Compute_PerfectSignals_ReturnsFullScore()
    {
        var report = _service.Compute(new HealthSignals(0, 0, 6, 6));
        report.StartupScore.Should().Be(100);
        report.BackgroundServiceScore.Should().Be(100);
        report.ReadinessScore.Should().Be(100);
        report.Overall.Should().Be(100);
        report.Grade.Should().Be("优秀");
    }

    [Fact]
    public void Compute_FirstThreeStartupItems_DoNotPenalize()
    {
        _service.Compute(new HealthSignals(3, 0, 0, 6)).StartupScore.Should().Be(100);
        _service.Compute(new HealthSignals(5, 0, 0, 6)).StartupScore.Should().Be(90);
    }

    [Fact]
    public void Compute_BackgroundServicesReduceScore()
    {
        _service.Compute(new HealthSignals(0, 10, 0, 6)).BackgroundServiceScore.Should().Be(80);
    }

    [Fact]
    public void Compute_Readiness_IsAppliedRatio()
    {
        _service.Compute(new HealthSignals(0, 0, 3, 6)).ReadinessScore.Should().Be(50);
    }

    [Fact]
    public void Compute_NoGameRules_ReadinessIsZero_NotDivideByZero()
    {
        var report = _service.Compute(new HealthSignals(0, 0, 0, 0));
        report.ReadinessScore.Should().Be(0);
        report.Overall.Should().BeInRange(0, 100);
    }

    [Fact]
    public void Compute_OverallIsWeightedSum()
    {
        // startup=100*0.3 + background=80*0.3 + readiness=50*0.4 = 30 + 24 + 20 = 74
        var report = _service.Compute(new HealthSignals(0, 10, 3, 6));
        report.Overall.Should().Be(74);
    }

    [Fact]
    public void Compute_ScoresAreClamped()
    {
        var report = _service.Compute(new HealthSignals(1000, 1000, 0, 6));
        report.StartupScore.Should().Be(30);
        report.BackgroundServiceScore.Should().Be(30);
    }

    [Theory]
    [InlineData(100, "优秀")]
    [InlineData(85, "优秀")]
    [InlineData(84, "良好")]
    [InlineData(70, "良好")]
    [InlineData(69, "一般")]
    [InlineData(50, "一般")]
    [InlineData(49, "待优化")]
    public void Grade_MapsOverall(int overall, string expected)
    {
        HealthScoreService.Grade(overall).Should().Be(expected);
    }

    [Fact]
    public void Compute_BadSignals_ProducesSuggestions()
    {
        var report = _service.Compute(new HealthSignals(20, 40, 0, 6));
        report.Suggestions.Should().NotBeEmpty();
        report.Suggestions.Count.Should().BeLessThanOrEqualTo(3);
    }
}
