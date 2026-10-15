// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using PcOpt.Core;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 报告导出器（T34，PRD C-P0-3 / C-P0-6）：帧时间数据导出为 CSV，对比图 / 帧时间图导出为 PNG。
/// <para>CSV 采用 BOM + UTF-8（Excel 友好），并在头部注释区标注算法口径与关键指标。</para>
/// </summary>
public sealed class ReportExporter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// 导出帧时间明细 + 关键指标为 CSV。
    /// </summary>
    /// <param name="stats">帧统计。</param>
    /// <param name="samples">帧样本。</param>
    /// <param name="filePath">目标路径（.csv）。</param>
    public Result<string> ExportCsv(FrameStats stats, IReadOnlyList<FrameSample> samples, string filePath)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(samples);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<string>.Fail("导出路径为空");
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("# FrameLab 帧时间报告（CSV）");
            AppendMeta(builder, "生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AppendMeta(builder, "帧数", stats.FrameCount.ToString(CultureInfo.InvariantCulture));
            AppendMeta(builder, "时长(秒)", stats.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture));
            AppendMeta(builder, "平均 FPS", stats.AverageFps.ToString("F1", CultureInfo.InvariantCulture));
            AppendMeta(builder, "平均帧时间(ms)", stats.AverageFrameTimeMs.ToString("F3", CultureInfo.InvariantCulture));
            AppendMeta(builder, "中位帧时间(ms)", stats.MedianFrameTimeMs.ToString("F3", CultureInfo.InvariantCulture));
            AppendMeta(builder, "1% Low 时间占比法(ms)", stats.LowOnePercentTimeBasedMs.ToString("F3", CultureInfo.InvariantCulture));
            AppendMeta(builder, "1% Low 统计百分位法(ms)", stats.LowOnePercentPercentileMs.ToString("F3", CultureInfo.InvariantCulture));
            AppendMeta(builder, "卡顿次数", stats.StutterCount.ToString(CultureInfo.InvariantCulture));
            AppendMeta(builder, "卡顿阈值(ms)", stats.StutterThresholdMs.ToString("F3", CultureInfo.InvariantCulture));
            AppendMeta(builder, "DPC 平均(ms, 近似)", stats.Dpc.AverageMs.ToString("F3", CultureInfo.InvariantCulture));
            AppendMeta(builder, "口径说明", ComparisonAnalyzer.AlgorithmAnnotation);

            builder.AppendLine("Application,TimeInSeconds,FrameTimeMs,CpuMs,GpuMs,DisplayLatencyMs");
            foreach (var sample in samples)
            {
                builder.Append(Escape(sample.Application)).Append(',')
                    .Append(sample.TimeInSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.FrameTimeMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Optional(sample.CpuMs)).Append(',')
                    .Append(Optional(sample.GpuMs)).Append(',')
                    .Append(Optional(sample.DisplayLatencyMs))
                    .AppendLine();
            }

            WriteText(filePath, builder.ToString());
            return Result<string>.Ok($"已导出帧时间 CSV（{stats.FrameCount} 帧）到 {Path.GetFileName(filePath)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"导出 CSV 失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 导出前后对比为 CSV（指标 / 优化前 / 优化后 / 变化）。
    /// </summary>
    /// <param name="comparison">对比结果。</param>
    /// <param name="filePath">目标路径。</param>
    public Result<string> ExportComparisonCsv(ComparisonResult comparison, string filePath)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<string>.Fail("导出路径为空");
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("# FrameLab 优化前后对比（CSV）");
            AppendMeta(builder, "结论", comparison.VerdictText);
            AppendMeta(builder, "总结", comparison.Summary);
            AppendMeta(builder, "口径说明", comparison.AlgorithmNote);
            builder.AppendLine("指标,优化前,优化后,变化,结论");
            foreach (var row in comparison.Rows)
            {
                builder.Append(Escape(row.Name)).Append(',')
                    .Append(Escape(row.BeforeText)).Append(',')
                    .Append(Escape(row.AfterText)).Append(',')
                    .Append(Escape(row.DeltaText)).Append(',')
                    .Append(row.Verdict)
                    .AppendLine();
            }

            WriteText(filePath, builder.ToString());
            return Result<string>.Ok("已导出对比 CSV 到 " + Path.GetFileName(filePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"导出对比 CSV 失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 导出帧时间报告为 PNG（原生绘制）。
    /// <para>⚠️ 需要在 STA 线程（UI 线程）调用。</para>
    /// </summary>
    /// <param name="stats">帧统计。</param>
    /// <param name="samples">帧样本。</param>
    /// <param name="filePath">目标路径（.png）。</param>
    public Result<string> ExportPng(FrameStats stats, IReadOnlyList<FrameSample> samples, string filePath)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(samples);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<string>.Fail("导出路径为空");
        }

        try
        {
            var bitmap = ReportPngRenderer.Render(stats, samples);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
            return Result<string>.Ok("已导出报告图到 " + Path.GetFileName(filePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return Result<string>.Fail($"导出 PNG 失败：{ex.Message}");
        }
    }

    private static void AppendMeta(StringBuilder builder, string key, string value)
    {
        builder.Append("# ").Append(key).Append(',').Append(Escape(value)).AppendLine();
    }

    private static string Optional(double? value) =>
        value.HasValue ? value.Value.ToString("F3", CultureInfo.InvariantCulture) : string.Empty;

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static void WriteText(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content, Utf8WithBom);
    }
}
