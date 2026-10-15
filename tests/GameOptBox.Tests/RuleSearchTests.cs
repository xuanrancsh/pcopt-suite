// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>规则搜索 / 过滤测试（T21）。</summary>
public sealed class RuleSearchTests
{
    private static TweakRule Rule(string id, string category, RiskLevel risk, string name, string description = "") =>
        new()
        {
            Id = id,
            Category = category,
            Risk = risk,
            DisplayName = name,
            Description = description,
            Actions = new TweakAction[] { new TimerResolutionAction(5000) },
        };

    private static readonly TweakRule[] Sample =
    {
        Rule("perf.svc.sysmain.manual", "性能", RiskLevel.Notice, "让预读取服务改为按需启动", "减少后台磁盘读写"),
        Rule("game.timer.resolution", "游戏", RiskLevel.Notice, "把计时器精度调到 0.5 毫秒", "画面更跟手"),
        Rule("privacy.defender.realtime.disable", "隐私", RiskLevel.Danger, "关闭 Defender 实时保护", "降低安全性"),
        Rule("latency.mouse.acceleration.disable", "延迟", RiskLevel.Safe, "关闭鼠标加速度", "鼠标 1:1"),
    };

    [Fact]
    public void Matches_EmptyQuery_ReturnsTrue()
    {
        RuleSearch.Matches(Sample[0], null).Should().BeTrue();
        RuleSearch.Matches(Sample[0], "   ").Should().BeTrue();
    }

    [Theory]
    [InlineData("sysmain")]
    [InlineData("预读取")]
    [InlineData("性能")]
    [InlineData("磁盘")]
    public void Matches_KeywordHits_ReturnsTrue(string keyword)
    {
        RuleSearch.Matches(Sample[0], keyword).Should().BeTrue();
    }

    [Fact]
    public void Matches_IrrelevantKeyword_ReturnsFalse()
    {
        RuleSearch.Matches(Sample[0], "防水涂层").Should().BeFalse();
    }

    [Fact]
    public void Filter_ExcludesSensitiveByDefault()
    {
        var result = RuleSearch.Filter(Sample, null, null, includeSensitive: false);
        result.Should().NotContain(rule => rule.Risk == RiskLevel.Danger);
        result.Should().HaveCount(3);
    }

    [Fact]
    public void Filter_IncludesSensitiveWhenRequested()
    {
        var result = RuleSearch.Filter(Sample, null, null, includeSensitive: true);
        result.Should().HaveCount(4);
    }

    [Fact]
    public void Filter_ByCategory()
    {
        var result = RuleSearch.Filter(Sample, null, "游戏", includeSensitive: true);
        result.Should().ContainSingle().Which.Id.Should().Be("game.timer.resolution");
    }

    [Fact]
    public void Filter_ByCategoryAndKeyword()
    {
        var result = RuleSearch.Filter(Sample, "0.5", "游戏", includeSensitive: true);
        result.Should().ContainSingle().Which.Id.Should().Be("game.timer.resolution");
    }

    [Fact]
    public void DistinctCategories_SortedAndDistinct()
    {
        var categories = RuleSearch.DistinctCategories(Sample);
        categories.Should().OnlyHaveUniqueItems();
        categories.Should().BeEquivalentTo(new[] { "性能", "游戏", "隐私", "延迟" });
    }

    [Fact]
    public void IsSensitive_OnlyDanger()
    {
        RuleSearch.IsSensitive(Sample[2]).Should().BeTrue();
        RuleSearch.IsSensitive(Sample[0]).Should().BeFalse();
    }
}
