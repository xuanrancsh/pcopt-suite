// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>单行对比结论（决定 UI 颜色）。</summary>
public enum ComparisonRowVerdict
{
    /// <summary>变好（绿）。</summary>
    Better,

    /// <summary>持平（灰）。</summary>
    Same,

    /// <summary>变差（红）。</summary>
    Worse,
}

/// <summary>整体对比结论（PRD C-P0-5 / C-P0-6："有效 / 无效 / 不稳定"）。</summary>
public enum ComparisonVerdict
{
    /// <summary>有效（相关指标明显改善且未损害稳定性）。</summary>
    Improved,

    /// <summary>无效 / 持平（在噪声范围内，未观察到改善）。</summary>
    Same,

    /// <summary>退化（相关指标明显变差）。</summary>
    Worse,

    /// <summary>不稳定（有改善但稳定性下降，或指标互相矛盾）。</summary>
    Unstable,
}

/// <summary>一行指标对比。</summary>
/// <param name="Name">指标人话名。</param>
/// <param name="BeforeText">优化前展示值。</param>
/// <param name="AfterText">优化后展示值。</param>
/// <param name="DeltaText">变化量展示（如 "+6.3%"）。</param>
/// <param name="Verdict">结论。</param>
public sealed record ComparisonRow(
    string Name,
    string BeforeText,
    string AfterText,
    string DeltaText,
    ComparisonRowVerdict Verdict);

/// <summary>前后对比结果。</summary>
/// <param name="Verdict">整体结论。</param>
/// <param name="VerdictText">结论人话（有效 / 无效 / 退化 / 不稳定）。</param>
/// <param name="Summary">一句话总结（含关键数字）。</param>
/// <param name="Rows">指标对比行。</param>
/// <param name="AlgorithmNote">口径说明（1% Low 双算法 + HAGS 影响，PRD 4.7-3）。</param>
public sealed record ComparisonResult(
    ComparisonVerdict Verdict,
    string VerdictText,
    string Summary,
    IReadOnlyList<ComparisonRow> Rows,
    string AlgorithmNote);

/// <summary>
/// 前后对比分析器（T36 / PRD C-P0-6）：基于两次采集的 <see cref="FrameStats"/> 自动生成
/// 指标对比与"有效 / 无效 / 退化 / 不稳定"结论。纯函数、可复现。
/// </summary>
public static class ComparisonAnalyzer
{
    /// <summary>判定"明显变化"的阈值（百分比）。</summary>
    public const double ThresholdPercent = 2.0;

    /// <summary>稳定性恶化阈值：标准差上涨比例。</summary>
    private const double StdDevWorseRatio = 1.25;

    /// <summary>稳定性恶化阈值：每分钟卡顿数上涨比例。</summary>
    private const double StutterWorseRatio = 1.30;

    /// <summary>1% Low 口径说明（报告与对比页均须展示，PRD 4.7-3）。</summary>
    public const string AlgorithmAnnotation =
        "1% Low 采用两种口径：① 统计百分位法——帧时间排序后取第 99 百分位（最慢的 1% 单帧水平）；" +
        "② 时间占比法——累计最慢 1% 时长内的平均帧时间。两者会给出不同数值属正常。" +
        "另：HAGS（硬件加速 GPU 计划）是否开启会改变帧时间的采集口径，开关前后不宜直接比较绝对值。";

    /// <summary>对比两次采集，产出结论。</summary>
    /// <param name="before">优化前统计。</param>
    /// <param name="after">优化后统计。</param>
    public static ComparisonResult Compare(FrameStats before, FrameStats after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var avgGain = GainPercent(before.AverageFps, after.AverageFps);
        var lowGain = GainPercent(before.LowOnePercentFps(LowOnePercentAlgorithm.Percentile), after.LowOnePercentFps(LowOnePercentAlgorithm.Percentile));
        var lowTimeGain = GainPercent(before.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased), after.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased));
        var stutterDelta = after.StuttersPerMinute - before.StuttersPerMinute;

        var fpsBetter = avgGain >= ThresholdPercent;
        var fpsWorse = avgGain <= -ThresholdPercent;
        var lowBetter = lowGain >= ThresholdPercent;
        var lowWorse = lowGain <= -ThresholdPercent;

        var varianceWorse =
            (before.StdDevFrameTimeMs > 0 && after.StdDevFrameTimeMs > before.StdDevFrameTimeMs * StdDevWorseRatio)
            || (before.StuttersPerMinute > 0 && after.StuttersPerMinute > before.StuttersPerMinute * StutterWorseRatio && stutterDelta >= 1);

        ComparisonVerdict verdict;
        if (fpsWorse || lowWorse)
        {
            verdict = ComparisonVerdict.Worse;
        }
        else if ((fpsBetter || lowBetter) && !varianceWorse)
        {
            verdict = ComparisonVerdict.Improved;
        }
        else if ((fpsBetter || lowBetter) && varianceWorse)
        {
            verdict = ComparisonVerdict.Unstable;
        }
        else if (!fpsBetter && !lowBetter && !fpsWorse && !lowWorse)
        {
            verdict = ComparisonVerdict.Same;
        }
        else
        {
            verdict = ComparisonVerdict.Unstable;
        }

        var rows = new List<ComparisonRow>
        {
            new("平均帧率", Fps(before.AverageFps), Fps(after.AverageFps), Percent(avgGain), VerdictForGain(avgGain)),
            new("1% Low（百分位法）", Fps(before.LowOnePercentFps(LowOnePercentAlgorithm.Percentile)), Fps(after.LowOnePercentFps(LowOnePercentAlgorithm.Percentile)), Percent(lowGain), VerdictForGain(lowGain)),
            new("1% Low（时间占比法）", Fps(before.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased)), Fps(after.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased)), Percent(lowTimeGain), VerdictForGain(lowTimeGain)),
            new("卡顿次数 / 分钟", PerMinute(before.StuttersPerMinute), PerMinute(after.StuttersPerMinute), SignedNumber(stutterDelta, " 次"), VerdictForStutter(stutterDelta)),
            new("平均帧时间", Milliseconds(before.AverageFrameTimeMs), Milliseconds(after.AverageFrameTimeMs), Percent(-GainPercent(before.AverageFrameTimeMs, after.AverageFrameTimeMs)), VerdictForGain(-GainPercent(before.AverageFrameTimeMs, after.AverageFrameTimeMs))),
        };

        return new ComparisonResult(
            verdict,
            VerdictText(verdict),
            BuildSummary(verdict, avgGain, lowGain, stutterDelta),
            rows,
            AlgorithmAnnotation);
    }

    /// <summary>结论人话。</summary>
    public static string VerdictText(ComparisonVerdict verdict) => verdict switch
    {
        ComparisonVerdict.Improved => "有效：优化后体验变好",
        ComparisonVerdict.Same => "无效：与优化前基本一致",
        ComparisonVerdict.Worse => "退化：优化后反而变差，建议还原",
        _ => "不稳定：有改善但稳定性下降，请谨慎保留",
    };

    private static string BuildSummary(ComparisonVerdict verdict, double avgGain, double lowGain, double stutterDelta)
    {
        var core = $"平均帧率 {Signed(avgGain)}，1% Low {Signed(lowGain)}";
        var extra = Math.Abs(stutterDelta) >= 0.5 ? $"，卡顿 {SignedNumber(stutterDelta, " 次/分")}" : string.Empty;
        return verdict switch
        {
            ComparisonVerdict.Improved => $"优化见效：{core}{extra}。",
            ComparisonVerdict.Same => $"看不出明显变化（在测量噪声范围内）：{core}{extra}，可考虑关闭这些调整省点事。",
            ComparisonVerdict.Worse => $"优化后变差了：{core}{extra}，建议一键还原。",
            _ => $"结果不稳定：{core}{extra}，部分指标改善但稳定性下降。",
        };
    }

    private static ComparisonRowVerdict VerdictForGain(double gainPercent)
    {
        if (gainPercent >= ThresholdPercent)
        {
            return ComparisonRowVerdict.Better;
        }

        return gainPercent <= -ThresholdPercent ? ComparisonRowVerdict.Worse : ComparisonRowVerdict.Same;
    }

    private static ComparisonRowVerdict VerdictForStutter(double delta)
    {
        if (delta <= -0.5)
        {
            return ComparisonRowVerdict.Better;
        }

        return delta >= 0.5 ? ComparisonRowVerdict.Worse : ComparisonRowVerdict.Same;
    }

    /// <summary>(after - before) / before × 100；before 为 0 时按符号返回。</summary>
    private static double GainPercent(double before, double after)
    {
        if (before <= 0)
        {
            return after > 0 ? 100 : 0;
        }

        return (after - before) / before * 100.0;
    }

    private static string Fps(double value) => value.ToString("F0", CultureInfo.InvariantCulture) + " FPS";

    private static string Milliseconds(double value) => value.ToString("F2", CultureInfo.InvariantCulture) + " ms";

    private static string PerMinute(double value) => value.ToString("F1", CultureInfo.InvariantCulture) + " 次";

    private static string Percent(double value) => Signed(value) + "%";

    private static string Signed(double value) => (value >= 0 ? "+" : string.Empty) + value.ToString("F1", CultureInfo.InvariantCulture);

    private static string SignedNumber(double value, string unit) =>
        (value >= 0 ? "+" : string.Empty) + value.ToString("F1", CultureInfo.InvariantCulture) + unit;
}
