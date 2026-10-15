// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>前后对比结论测试（T36 / PRD C-P0-5 / C-P0-6）：有效 / 无效 / 退化 / 不稳定。</summary>
public sealed class ComparisonAnalyzerTests
{
    private static FrameStats Stats(double avgFps, double lowMs, double stdDev = 0, int stutters = 0, double durationSeconds = 60)
    {
        return new FrameStats
        {
            FrameCount = 1000,
            AverageFps = avgFps,
            AverageFrameTimeMs = avgFps > 0 ? 1000.0 / avgFps : 0,
            MedianFrameTimeMs = avgFps > 0 ? 1000.0 / avgFps : 0,
            LowOnePercentTimeBasedMs = lowMs,
            LowOnePercentPercentileMs = lowMs,
            StdDevFrameTimeMs = stdDev,
            Stutters = Enumerable.Range(0, stutters).Select(i => new StutterEvent(i, i, lowMs, 3)).ToArray(),
            DurationSeconds = durationSeconds,
        };
    }

    [Fact]
    public void Compare_BetterAndStable_IsImproved()
    {
        var result = ComparisonAnalyzer.Compare(Stats(60, 50, stdDev: 1.0), Stats(70, 45, stdDev: 1.0));

        result.Verdict.Should().Be(ComparisonVerdict.Improved);
        result.VerdictText.Should().Contain("有效");
        result.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Compare_WorseAverage_IsWorse()
    {
        var result = ComparisonAnalyzer.Compare(Stats(60, 50), Stats(50, 42));
        result.Verdict.Should().Be(ComparisonVerdict.Worse);
        result.VerdictText.Should().Contain("退化");
    }

    [Fact]
    public void Compare_NoMeaningfulChange_IsSame()
    {
        var result = ComparisonAnalyzer.Compare(Stats(60, 50), Stats(60.5, 50.2));
        result.Verdict.Should().Be(ComparisonVerdict.Same);
        result.VerdictText.Should().Contain("无效");
    }

    [Fact]
    public void Compare_BetterButLessStable_IsUnstable()
    {
        var result = ComparisonAnalyzer.Compare(Stats(60, 50, stdDev: 1.0), Stats(70, 45, stdDev: 2.0));
        result.Verdict.Should().Be(ComparisonVerdict.Unstable);
        result.VerdictText.Should().Contain("不稳定");
    }

    [Fact]
    public void Compare_MoreStutters_IsUnstableOrWorse()
    {
        // 帧率略升但卡顿显著增加 → 不应判为"有效"
        var result = ComparisonAnalyzer.Compare(Stats(60, 50, stutters: 2), Stats(62, 50, stutters: 8));
        result.Verdict.Should().NotBe(ComparisonVerdict.Improved);
    }

    [Fact]
    public void Compare_ProducesFiveRowsIncludingBothLowAlgorithms()
    {
        var result = ComparisonAnalyzer.Compare(Stats(60, 50), Stats(70, 45));

        result.Rows.Should().HaveCount(5);
        result.Rows.Select(r => r.Name).Should().Contain("平均帧率");
        result.Rows.Select(r => r.Name).Should().Contain("1% Low（百分位法）");
        result.Rows.Select(r => r.Name).Should().Contain("1% Low（时间占比法）");
    }

    [Fact]
    public void Compare_AlgorithmNote_MentionsBothAlgorithmsAndHags()
    {
        var result = ComparisonAnalyzer.Compare(Stats(60, 50), Stats(70, 45));

        result.AlgorithmNote.Should().Contain("1% Low");
        result.AlgorithmNote.Should().Contain("HAGS");
        result.AlgorithmNote.Should().Contain("百分位");
    }
}
