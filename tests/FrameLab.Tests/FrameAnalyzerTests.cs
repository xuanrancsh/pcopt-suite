// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>帧时间分析测试（T33 / PRD C-P0-3）：平均 FPS、1% Low 双算法、卡顿、分布、DPC。</summary>
public sealed class FrameAnalyzerTests
{
    private static IReadOnlyList<FrameSample> Samples(params double[] frameTimes) =>
        frameTimes.Select((t, i) => new FrameSample { FrameTimeMs = t, TimeInSeconds = i * 0.016 }).ToArray();

    [Fact]
    public void Analyze_AverageFps_IsHandComputable()
    {
        // 4 帧 × 16ms → 平均帧时间 16ms → 62.5 FPS
        var stats = FrameAnalyzer.Analyze(Samples(16, 16, 16, 16));

        stats.FrameCount.Should().Be(4);
        stats.AverageFrameTimeMs.Should().BeApproximately(16.0, 1e-9);
        stats.AverageFps.Should().BeApproximately(62.5, 1e-9);
        stats.MedianFrameTimeMs.Should().BeApproximately(16.0, 1e-9);
    }

    [Fact]
    public void Analyze_LowOnePercent_BothAlgorithms_AreHandComputable()
    {
        // 99 帧 10ms + 1 帧 1000ms：总时长 1990ms
        var times = Enumerable.Repeat(10.0, 99).Append(1000.0).ToArray();
        var stats = FrameAnalyzer.Analyze(Samples(times));

        // 时间占比法：最慢帧 1000ms 已 ≥ 总时长 1%(19.9ms) → 只取该帧 → 1000ms
        stats.LowOnePercentTimeBasedMs.Should().BeApproximately(1000.0, 1e-9);
        // 统计百分位法：rank = ceil(0.99 × 100) = 99 → 升序第 99 个 = 10ms
        stats.LowOnePercentPercentileMs.Should().BeApproximately(10.0, 1e-9);

        // 两口径换算 FPS
        stats.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased).Should().BeApproximately(1.0, 1e-9);
        stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile).Should().BeApproximately(100.0, 1e-9);

        // 两个口径确实给出不同数值（报告必须同时标注）
        stats.LowOnePercentTimeBasedMs.Should().NotBe(stats.LowOnePercentPercentileMs);
    }

    [Fact]
    public void Analyze_LowOnePercent_TimeBased_AccumulatesUntilOnePercent()
    {
        // 100 帧：其中 2 帧为 200ms，其余 98 帧为 10ms → 总时长 = 400 + 980 = 1380ms
        var times = Enumerable.Repeat(10.0, 98).Concat(new[] { 200.0, 200.0 }).ToArray();
        var stats = FrameAnalyzer.Analyze(Samples(times));

        // 目标 = 13.8ms；最慢帧 200 已足够 → 只取 1 帧 → 200ms
        stats.LowOnePercentTimeBasedMs.Should().BeApproximately(200.0, 1e-9);
    }

    [Fact]
    public void Analyze_Percentile_RankIsCeilOf99Percent()
    {
        // n=200，rank = ceil(0.99×200) = 198 → 升序第 198 个
        var times = Enumerable.Range(1, 200).Select(i => (double)i).ToArray();
        var stats = FrameAnalyzer.Analyze(Samples(times));

        stats.LowOnePercentPercentileMs.Should().BeApproximately(198.0, 1e-9);
    }

    [Fact]
    public void Analyze_Stutter_RequiresStrictlyGreaterThanTwiceMedian()
    {
        // 中位数 10ms → 阈值 20ms；20 不算，20.0001 才算
        var exact = FrameAnalyzer.Analyze(Samples(10, 10, 10, 10, 10, 20));
        exact.MedianFrameTimeMs.Should().BeApproximately(10.0, 1e-9);
        exact.StutterThresholdMs.Should().BeApproximately(20.0, 1e-9);
        exact.StutterCount.Should().Be(0);

        var above = FrameAnalyzer.Analyze(Samples(10, 10, 10, 10, 10, 20.0001));
        above.StutterCount.Should().Be(1);
        above.Stutters[0].MultipleOfMedian.Should().BeGreaterThan(2.0);
    }

    [Fact]
    public void Analyze_Stutter_CountsSpikesAndComputesMultiple()
    {
        var stats = FrameAnalyzer.Analyze(Samples(10, 10, 30, 10, 10, 10));

        stats.StutterCount.Should().Be(1);
        stats.Stutters[0].FrameTimeMs.Should().BeApproximately(30.0, 1e-9);
        stats.Stutters[0].MultipleOfMedian.Should().BeApproximately(3.0, 1e-9);
        stats.StuttersPerMinute.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Analyze_IgnoresNonPositiveAndNonFiniteFrameTimes()
    {
        var samples = new[]
        {
            new FrameSample { FrameTimeMs = 16 },
            new FrameSample { FrameTimeMs = 0 },
            new FrameSample { FrameTimeMs = -3 },
            new FrameSample { FrameTimeMs = double.NaN },
        };

        var stats = FrameAnalyzer.Analyze(samples);

        stats.FrameCount.Should().Be(1);
        stats.AverageFrameTimeMs.Should().BeApproximately(16.0, 1e-9);
    }

    [Fact]
    public void Analyze_EmptySamples_ReturnsEmptyStats()
    {
        var stats = FrameAnalyzer.Analyze(Array.Empty<FrameSample>());
        stats.FrameCount.Should().Be(0);
        stats.AverageFps.Should().Be(0);
        stats.Stutters.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_StandardDeviation_IsPopulation()
    {
        // 均值 15 → 总体标准差 = sqrt(((10-15)^2 + (20-15)^2)/2) = 5
        var stats = FrameAnalyzer.Analyze(Samples(10, 20));
        stats.StdDevFrameTimeMs.Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void Analyze_Distribution_BucketsByFpsRange()
    {
        var stats = FrameAnalyzer.Analyze(Samples(5, 10, 20, 40, 60, 120));

        stats.Distribution.Should().HaveCount(6);
        stats.Distribution.Sum(b => b.Count).Should().Be(6);
        stats.Distribution.Single(b => b.Label == "≥120 FPS").Count.Should().Be(1);
        stats.Distribution.Single(b => b.Label == "<10 FPS").Count.Should().Be(1);
    }

    [Fact]
    public void Analyze_PassesThroughDpcStats()
    {
        var dpc = new DpcStats(1.5, 8.0, 200);
        var stats = FrameAnalyzer.Analyze(Samples(16, 16), dpc);

        stats.Dpc.Should().Be(dpc);
        stats.Dpc.IsElevated.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0, 0.0, 0, false)]  // 无采样
    [InlineData(0.5, 3.0, 100, false)] // 平均与峰值都低
    [InlineData(1.0, 0.0, 100, true)]  // 平均达阈值
    [InlineData(0.2, 5.0, 100, true)]  // 峰值达阈值
    public void DpcStats_IsElevated_Thresholds(double avg, double max, int samples, bool expected)
    {
        new DpcStats(avg, max, samples).IsElevated.Should().Be(expected);
        DpcStats.Empty.IsElevated.Should().BeFalse();
    }
}
