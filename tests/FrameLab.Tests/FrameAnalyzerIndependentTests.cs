// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// 1% Low 双算法「口径透明」独立复算（QA 构造，T37 重点 1 / PRD 4.7-3）。
/// 不采信工程师自报：独立构造受控序列，<b>手推两款算法的期望值</b>，与实现逐一对拍；
/// 并验证两口径确实同时计算、卡顿阈值严格大于、边界正确。
/// </summary>
public sealed class FrameAnalyzerIndependentTests
{
    private static IReadOnlyList<FrameSample> Ramp(int n)
    {
        // 帧时间 1,2,…,n（毫秒），时刻按累计补齐
        var samples = new List<FrameSample>(n);
        var t = 0.0;
        for (var i = 1; i <= n; i++)
        {
            samples.Add(new FrameSample { FrameTimeMs = i, TimeInSeconds = t / 1000.0 });
            t += i;
        }

        return samples;
    }

    [Fact]
    public void UniformRamp_Algorithms_MatchHandComputation()
    {
        // 序列 1..1000：总时长 = 500500ms。
        // 时间占比法：目标 = 500500 × 1% = 5005ms；从最慢帧累加：
        //   k=5 → 1000+999+998+997+996 = 4990 < 5005；k=6 → +995 = 5985 ≥ 5005
        //   ⇒ 取 6 帧，均值 = 5985 / 6 = 997.5
        // 统计百分位法：rank = ceil(0.99 × 1000) = 990 ⇒ 升序第 990 个 = 990
        // 中位数（偶数） = (500 + 501) / 2 = 500.5 ⇒ 卡顿阈值 = 1001（最大值 1000 < 1001，无卡顿）
        var stats = FrameAnalyzer.Analyze(Ramp(1000));

        stats.FrameCount.Should().Be(1000);
        stats.MedianFrameTimeMs.Should().BeApproximately(500.5, 1e-9);
        stats.LowOnePercentTimeBasedMs.Should().BeApproximately(997.5, 1e-9, "时间占比法手算期望 5985/6");
        stats.LowOnePercentPercentileMs.Should().BeApproximately(990.0, 1e-9, "百分位法 rank=990 → 第 990 个");
        stats.StutterThresholdMs.Should().BeApproximately(1001.0, 1e-9);
        stats.StutterCount.Should().Be(0, "最大值 1000 不大于阈值 1001");
    }

    [Fact]
    public void HeavyTail_TimeBasedVsPercentile_Diverge_AndBothPopulated()
    {
        // 990 帧 10ms + 10 帧 100ms。
        // 总时长 = 9900 + 1000 = 10900；目标 = 109。
        // 时间占比法：最慢 1 帧 100 < 109 ⇒ 需要 2 帧，均值 = 200/2 = 100
        // 百分位法：rank = ceil(0.99 × 1000) = 990 ⇒ 升序第 990 个 = 10（前 990 个都是 10）
        var times = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(100.0, 10)).ToArray();
        var stats = FrameAnalyzer.Analyze(Samples(times));

        stats.LowOnePercentTimeBasedMs.Should().BeApproximately(100.0, 1e-9);
        stats.LowOnePercentPercentileMs.Should().BeApproximately(10.0, 1e-9);
        stats.LowOnePercentTimeBasedMs.Should().NotBe(stats.LowOnePercentPercentileMs,
            "两款算法口径不同，必须给出不同数值（报告须同时标注）");

        // 两口径换算 FPS 均有效且 > 0
        stats.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased).Should().BeApproximately(10.0, 1e-9);
        stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile).Should().BeApproximately(100.0, 1e-9);
    }

    [Fact]
    public void SingleSample_BothAlgorithms_EqualThatSample()
    {
        var stats = FrameAnalyzer.Analyze(Samples(16.0));

        stats.LowOnePercentTimeBasedMs.Should().BeApproximately(16.0, 1e-9);
        stats.LowOnePercentPercentileMs.Should().BeApproximately(16.0, 1e-9);
    }

    [Fact]
    public void Percentile_RankIsCeilOf99Percent_ForOddAndEvenCounts()
    {
        // n=101：rank = ceil(99.99) = 100 → 升序第 100 个
        var odd = FrameAnalyzer.Analyze(Samples(Enumerable.Range(1, 101).Select(i => (double)i).ToArray()));
        odd.LowOnePercentPercentileMs.Should().BeApproximately(100.0, 1e-9);

        // n=100：rank = ceil(99) = 99 → 升序第 99 个
        var even = FrameAnalyzer.Analyze(Samples(Enumerable.Range(1, 100).Select(i => (double)i).ToArray()));
        even.LowOnePercentPercentileMs.Should().BeApproximately(99.0, 1e-9);
    }

    [Fact]
    public void Stutter_StrictlyGreaterThanTwiceMedian()
    {
        // 中位数 8 → 阈值 16；16 不算，16.0001 才算
        var atThreshold = FrameAnalyzer.Analyze(Samples(8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 16));
        atThreshold.MedianFrameTimeMs.Should().BeApproximately(8.0, 1e-9);
        atThreshold.StutterThresholdMs.Should().BeApproximately(16.0, 1e-9);
        atThreshold.StutterCount.Should().Be(0, "等于 2× 中位数不算卡顿");

        var justAbove = FrameAnalyzer.Analyze(Samples(8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 16.0001));
        justAbove.StutterCount.Should().Be(1);
        justAbove.Stutters[0].MultipleOfMedian.Should().BeGreaterThan(2.0);
    }

    [Fact]
    public void Stutter_WithNegativeOrZeroMedian_CannotOccur_BecauseTimesArePositives()
    {
        // FrameAnalyzer 只接收 >0 且有限的帧时间 → 中位数恒 > 0，
        // FindStutters 的 medianMs<=0 分支是防御性代码（经审查确认，不可由公开入口触达）。
        var stats = FrameAnalyzer.Analyze(Samples(0, -1, double.NaN, double.PositiveInfinity));
        stats.FrameCount.Should().Be(0, "非正 / 非有限帧时间全部被过滤");
        stats.Stutters.Should().BeEmpty();
        stats.StutterThresholdMs.Should().Be(0);
    }

    private static IReadOnlyList<FrameSample> Samples(params double[] frameTimes) =>
        frameTimes.Select((t, i) => new FrameSample { FrameTimeMs = t, TimeInSeconds = i * 0.016 }).ToArray();
}
