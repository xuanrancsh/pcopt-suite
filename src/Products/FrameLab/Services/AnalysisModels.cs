// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 1% Low 计算方法（PRD C-P0-3：两种算法都要算并在报告中标注口径，PRD 4.7-3）。
/// </summary>
public enum LowOnePercentAlgorithm
{
    /// <summary>
    /// 时间占比法：按帧时间从大到小累加，取累计时长达到总时长 1% 的最慢若干帧求均值。
    /// 反映"最卡的那 1% 时间"体验。
    /// </summary>
    TimeBased,

    /// <summary>
    /// 统计百分位法：对帧时间排序后取第 99 百分位对应的帧时间值。
    /// 反映"最慢 1% 的帧"的单帧水平。
    /// </summary>
    Percentile,
}

/// <summary>一次卡顿事件（帧时间 &gt; 2× 中位数）。</summary>
/// <param name="Index">帧序号（0 基）。</param>
/// <param name="TimeInSeconds">相对采集起点的时刻（秒）。</param>
/// <param name="FrameTimeMs">该帧帧时间（毫秒）。</param>
/// <param name="MultipleOfMedian">相对中位数的倍数。</param>
public sealed record StutterEvent(int Index, double TimeInSeconds, double FrameTimeMs, double MultipleOfMedian);

/// <summary>帧时间分布直方图的一个桶。</summary>
/// <param name="LowerMs">下界（含，毫秒）。</param>
/// <param name="UpperMs">上界（不含，毫秒；最后一桶为 <see cref="double.PositiveInfinity"/>）。</param>
/// <param name="Label">人话标签（如"60-120 FPS"）。</param>
/// <param name="Count">落入该桶的帧数。</param>
public sealed record FrameTimeBucket(double LowerMs, double UpperMs, string Label, int Count);

/// <summary>DPC / 中断延迟统计（近似：调度抖动采样，口径见报告说明）。</summary>
/// <param name="AverageMs">平均延迟（毫秒）。</param>
/// <param name="MaxMs">最大延迟（毫秒）。</param>
/// <param name="Samples">采样点数。</param>
public sealed record DpcStats(double AverageMs, double MaxMs, int Samples)
{
    /// <summary>无采样数据时的空统计。</summary>
    public static DpcStats Empty { get; } = new(0, 0, 0);

    /// <summary>是否偏高（存在明显中断/调度延迟，可能引发帧时间尖峰）。</summary>
    public bool IsElevated => Samples > 0 && (AverageMs >= 1.0 || MaxMs >= 5.0);
}

/// <summary>温度 / 功耗曲线上的一个采样点（PRD C-P0-3）。</summary>
/// <param name="TimeInSeconds">相对采集起点的时刻（秒）。</param>
/// <param name="CpuTempC">CPU 温度（℃；不可用时 null）。</param>
/// <param name="GpuTempC">GPU 温度（℃；不可用时 null）。</param>
/// <param name="PackagePowerW">整机 / 封装功耗（W；不可用时 null）。</param>
public sealed record ThermalPoint(double TimeInSeconds, double? CpuTempC, double? GpuTempC, double? PackagePowerW);

/// <summary>
/// 帧时间分析结果（T33，PRD C-P0-3）。
/// 所有时间字段单位为毫秒；FPS 由帧时间换算（1000 / ms）。
/// </summary>
public sealed record FrameStats
{
    /// <summary>参与统计的帧数。</summary>
    public int FrameCount { get; init; }

    /// <summary>平均 FPS。</summary>
    public double AverageFps { get; init; }

    /// <summary>平均帧时间（毫秒）。</summary>
    public double AverageFrameTimeMs { get; init; }

    /// <summary>中位帧时间（毫秒）。</summary>
    public double MedianFrameTimeMs { get; init; }

    /// <summary>1% Low 帧时间（时间占比法，毫秒）。</summary>
    public double LowOnePercentTimeBasedMs { get; init; }

    /// <summary>1% Low 帧时间（统计百分位法，毫秒）。</summary>
    public double LowOnePercentPercentileMs { get; init; }

    /// <summary>最小帧时间（毫秒，最快帧）。</summary>
    public double MinFrameTimeMs { get; init; }

    /// <summary>最大帧时间（毫秒，最慢帧）。</summary>
    public double MaxFrameTimeMs { get; init; }

    /// <summary>帧时间总体标准差（毫秒）。</summary>
    public double StdDevFrameTimeMs { get; init; }

    /// <summary>卡顿阈值（2× 中位数帧时间，毫秒）。</summary>
    public double StutterThresholdMs { get; init; }

    /// <summary>卡顿事件列表（帧时间 &gt; 2× 中位数）。</summary>
    public IReadOnlyList<StutterEvent> Stutters { get; init; } = Array.Empty<StutterEvent>();

    /// <summary>帧时间分布直方图。</summary>
    public IReadOnlyList<FrameTimeBucket> Distribution { get; init; } = Array.Empty<FrameTimeBucket>();

    /// <summary>DPC / 中断延迟统计（近似）。</summary>
    public DpcStats Dpc { get; init; } = DpcStats.Empty;

    /// <summary>温度 / 功耗曲线（可为空）。</summary>
    public IReadOnlyList<ThermalPoint> Thermal { get; init; } = Array.Empty<ThermalPoint>();

    /// <summary>卡顿事件数。</summary>
    public int StutterCount => Stutters.Count;

    /// <summary>采集时长（秒）。</summary>
    public double DurationSeconds { get; init; }

    /// <summary>每分钟卡顿次数。</summary>
    public double StuttersPerMinute => DurationSeconds > 0 ? Stutters.Count * 60.0 / DurationSeconds : 0;

    /// <summary>按指定算法取 1% Low 帧时间（毫秒）。</summary>
    /// <param name="algorithm">算法口径。</param>
    public double LowOnePercentFrameTimeMs(LowOnePercentAlgorithm algorithm) =>
        algorithm == LowOnePercentAlgorithm.TimeBased ? LowOnePercentTimeBasedMs : LowOnePercentPercentileMs;

    /// <summary>按指定算法取 1% Low FPS。</summary>
    /// <param name="algorithm">算法口径。</param>
    public double LowOnePercentFps(LowOnePercentAlgorithm algorithm)
    {
        var frameTime = LowOnePercentFrameTimeMs(algorithm);
        return frameTime > 0 ? 1000.0 / frameTime : 0;
    }

    /// <summary>空统计（无数据）。</summary>
    public static FrameStats Empty { get; } = new();
}
