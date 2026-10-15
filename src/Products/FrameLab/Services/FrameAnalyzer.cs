// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 帧时间分析器（T33，PRD C-P0-3）。
/// <para>
/// 纯函数：输入帧样本 → 输出 <see cref="FrameStats"/>（平均 FPS、1% Low 双算法、帧时间分布、
/// 卡顿事件 &gt;2× 中位数、DPC 统计、温度/功耗曲线）。不触碰系统、不抛异常。
/// </para>
/// </summary>
public static class FrameAnalyzer
{
    /// <summary>1% Low 的采样比例（1%）。</summary>
    private const double LowOnePercentRatio = 0.01;

    /// <summary>卡顿判定倍数（帧时间 &gt; 2× 中位数）。</summary>
    private const double StutterMultiple = 2.0;

    /// <summary>
    /// 分析帧样本。
    /// </summary>
    /// <param name="samples">帧样本（可含 null 字段、乱序；本方法不修改入参）。</param>
    /// <param name="dpc">DPC / 中断延迟统计（可为 null）。</param>
    /// <param name="thermal">温度 / 功耗曲线（可为 null）。</param>
    public static FrameStats Analyze(
        IReadOnlyList<FrameSample> samples,
        DpcStats? dpc = null,
        IReadOnlyList<ThermalPoint>? thermal = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var times = new List<double>(samples.Count);
        foreach (var sample in samples)
        {
            if (sample is not null && sample.FrameTimeMs > 0 && double.IsFinite(sample.FrameTimeMs))
            {
                times.Add(sample.FrameTimeMs);
            }
        }

        if (times.Count == 0)
        {
            return FrameStats.Empty;
        }

        var totalMs = 0.0;
        for (var i = 0; i < times.Count; i++)
        {
            totalMs += times[i];
        }

        var averageMs = totalMs / times.Count;
        var ascending = times.ToList();
        ascending.Sort();
        var medianMs = Median(ascending);

        return new FrameStats
        {
            FrameCount = times.Count,
            AverageFps = averageMs > 0 ? 1000.0 / averageMs : 0,
            AverageFrameTimeMs = averageMs,
            MedianFrameTimeMs = medianMs,
            LowOnePercentTimeBasedMs = TimeBasedLowMs(times, totalMs),
            LowOnePercentPercentileMs = PercentileLowMs(ascending),
            MinFrameTimeMs = ascending[0],
            MaxFrameTimeMs = ascending[^1],
            StdDevFrameTimeMs = StandardDeviation(times, averageMs),
            StutterThresholdMs = medianMs * StutterMultiple,
            Stutters = FindStutters(samples, medianMs),
            Distribution = BuildDistribution(times),
            Dpc = dpc ?? DpcStats.Empty,
            Thermal = thermal ?? Array.Empty<ThermalPoint>(),
            DurationSeconds = totalMs / 1000.0,
        };
    }

    /// <summary>中位数（入参须已升序）。</summary>
    private static double Median(List<double> ascending)
    {
        var n = ascending.Count;
        if (n == 0)
        {
            return 0;
        }

        return (n & 1) == 1
            ? ascending[n / 2]
            : (ascending[n / 2 - 1] + ascending[n / 2]) / 2.0;
    }

    /// <summary>
    /// 时间占比法 1% Low：把帧时间从大到小累加，取累计时长达到总时长 1% 的最慢若干帧的平均帧时间。
    /// </summary>
    private static double TimeBasedLowMs(List<double> times, double totalMs)
    {
        var descending = times.ToList();
        descending.Sort(static (a, b) => b.CompareTo(a));
        var target = totalMs * LowOnePercentRatio;

        var accumulated = 0.0;
        var count = 0;
        for (var i = 0; i < descending.Count; i++)
        {
            accumulated += descending[i];
            count++;
            if (accumulated >= target)
            {
                break;
            }
        }

        return count > 0 ? accumulated / count : 0;
    }

    /// <summary>
    /// 统计百分位法 1% Low：升序后取第 99 百分位对应的帧时间（rank = ceil(0.99 × n)）。
    /// </summary>
    private static double PercentileLowMs(List<double> ascending)
    {
        var n = ascending.Count;
        if (n == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(0.99 * n);
        if (rank < 1)
        {
            rank = 1;
        }
        else if (rank > n)
        {
            rank = n;
        }

        return ascending[rank - 1];
    }

    /// <summary>总体标准差。</summary>
    private static double StandardDeviation(List<double> times, double mean)
    {
        if (times.Count < 2)
        {
            return 0;
        }

        var sumSquares = 0.0;
        for (var i = 0; i < times.Count; i++)
        {
            var diff = times[i] - mean;
            sumSquares += diff * diff;
        }

        return Math.Sqrt(sumSquares / times.Count);
    }

    /// <summary>卡顿事件：帧时间 &gt; 2× 中位数（严格大于，等于阈值不算）。</summary>
    private static List<StutterEvent> FindStutters(IReadOnlyList<FrameSample> samples, double medianMs)
    {
        var stutters = new List<StutterEvent>();
        if (medianMs <= 0)
        {
            return stutters;
        }

        var threshold = medianMs * StutterMultiple;
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (sample is null)
            {
                continue;
            }

            if (sample.FrameTimeMs > threshold)
            {
                stutters.Add(new StutterEvent(i, sample.TimeInSeconds, sample.FrameTimeMs, sample.FrameTimeMs / medianMs));
            }
        }

        return stutters;
    }

    /// <summary>帧时间分布直方图（固定"整 FPS 档"边界）。</summary>
    private static List<FrameTimeBucket> BuildDistribution(List<double> times)
    {
        var buckets = new List<FrameTimeBucket>
        {
            new(0, 8, "≥120 FPS", 0),
            new(8, 16, "60-120 FPS", 0),
            new(16, 33.34, "30-60 FPS", 0),
            new(33.34, 50, "20-30 FPS", 0),
            new(50, 100, "10-20 FPS", 0),
            new(100, double.PositiveInfinity, "<10 FPS", 0),
        };

        foreach (var time in times)
        {
            for (var i = 0; i < buckets.Count; i++)
            {
                if (time >= buckets[i].LowerMs && time < buckets[i].UpperMs)
                {
                    buckets[i] = buckets[i] with { Count = buckets[i].Count + 1 };
                    break;
                }
            }
        }

        return buckets;
    }
}
