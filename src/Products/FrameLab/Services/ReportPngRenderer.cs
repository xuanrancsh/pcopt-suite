// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 报告 PNG 渲染器（T34，PRD C-P0-3：图表可导出 PNG）。
/// 用 WPF 原生绘制（DrawingVisual + RenderTargetBitmap），<b>不引入任何第三方图表包</b>。
/// <para>
/// ⚠️ 线程要求：WPF 绘图对象要求 STA 线程，请在 UI 线程调用（或由测试在工作线程上以 STA 启动）。
/// </para>
/// </summary>
public static class ReportPngRenderer
{
    private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x2B));
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x22, 0x28, 0x36));
    private static readonly Brush Primary = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush TextPrimary = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));
    private static readonly Brush TextSecondary = new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2));
    private static readonly Brush Grid = new SolidColorBrush(Color.FromRgb(0x33, 0x3B, 0x4B));

    /// <summary>
    /// 冻结全部静态画笔（缺陷 D1 修复）。
    /// WPF 的 <see cref="Freezable"/>（含 <see cref="SolidColorBrush"/>）若未冻结，会与"首次创建的线程"绑定；
    /// 本类注释明确允许"测试在工作线程以 STA 启动"，未冻结的静态画笔在跨 STA 线程复用时将抛
    /// “无法使用属于与父 Freezable 不同的线程的 DependencyObject”。冻结后画笔只读、跨线程安全。
    /// </summary>
    static ReportPngRenderer()
    {
        Background.Freeze();
        Panel.Freeze();
        Primary.Freeze();
        Accent.Freeze();
        Warning.Freeze();
        TextPrimary.Freeze();
        TextSecondary.Freeze();
        Grid.Freeze();
    }

    /// <summary>
    /// 渲染一张帧时间报告图（标题 + 关键指标 + 帧时间折线 + 1% Low 标记）。
    /// </summary>
    /// <param name="stats">帧统计。</param>
    /// <param name="samples">帧样本（用于折线）。</param>
    /// <param name="width">图宽（像素）。</param>
    /// <param name="height">图高（像素）。</param>
    /// <param name="title">标题。</param>
    public static BitmapSource Render(
        FrameStats stats,
        IReadOnlyList<FrameSample> samples,
        double width = 900,
        double height = 480,
        string title = "FrameLab 帧时间报告")
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(samples);

        width = Math.Max(320, width);
        height = Math.Max(200, height);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));

            var titleText = Text(title, 22, TextPrimary, bold: true);
            dc.DrawText(titleText, new Point(28, 22));

            var subtitle = Text(
                $"平均 {stats.AverageFps.ToString("F0", CultureInfo.InvariantCulture)} FPS · 共 {stats.FrameCount} 帧 · 时长 {stats.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture)} 秒",
                13,
                TextSecondary);
            dc.DrawText(subtitle, new Point(28, 54));

            // 关键指标卡
            DrawMetric(dc, 28, 84, 200, "平均帧率", stats.AverageFps.ToString("F0", CultureInfo.InvariantCulture) + " FPS", Primary);
            DrawMetric(dc, 244, 84, 200, "1% Low（百分位）", stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile).ToString("F0", CultureInfo.InvariantCulture) + " FPS", Warning);
            DrawMetric(dc, 460, 84, 200, "卡顿次数", stats.StutterCount.ToString(CultureInfo.InvariantCulture) + " 次", Accent);

            // 折线区域
            // 缺陷 D3 修复：高度下限被钳到 200，而布局上边距 176 + 下边距 60 = 236，
            // 直接算 height-236 在 height∈[200,235] 时为负 → Rect 抛 ArgumentException。
            // 这里按真实内边距计算并对宽/高各兜底为正值，绝不构造负尺寸 Rect。
            const double plotLeft = 28;
            const double plotTop = 176;
            const double plotRightPadding = 28;
            const double plotBottomPadding = 60;
            var plot = new Rect(
                plotLeft,
                plotTop,
                Math.Max(1, width - plotLeft - plotRightPadding),
                Math.Max(1, height - plotTop - plotBottomPadding));
            dc.DrawRectangle(Panel, null, plot);

            var gridPen = new Pen(Grid, 1) { DashStyle = DashStyles.Dot };
            for (var i = 0; i <= 4; i++)
            {
                var y = plot.Top + plot.Height * i / 4;
                dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            DrawFrameLine(dc, plot, samples, stats);

            var footnote = Text(
                "1% Low：百分位法（第 99 百分位帧时间）与时间占比法（累计最慢 1% 时长）双口径；HAGS 会改变帧时间采集口径。",
                11,
                TextSecondary);
            dc.DrawText(footnote, new Point(28, height - 40));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void DrawMetric(DrawingContext dc, double x, double y, double width, string label, string value, Brush accent)
    {
        var rect = new Rect(x, y, width, 76);
        dc.DrawRoundedRectangle(Panel, null, rect, 8, 8);
        dc.DrawRectangle(accent, null, new Rect(x, y + 14, 4, 48));
        dc.DrawText(Text(label, 12, TextSecondary), new Point(x + 16, y + 14));
        dc.DrawText(Text(value, 24, TextPrimary, bold: true), new Point(x + 16, y + 32));
    }

    private static void DrawFrameLine(DrawingContext dc, Rect plot, IReadOnlyList<FrameSample> samples, FrameStats stats)
    {
        var times = new List<double>(samples.Count);
        foreach (var sample in samples)
        {
            if (sample is not null && sample.FrameTimeMs > 0 && double.IsFinite(sample.FrameTimeMs))
            {
                times.Add(sample.FrameTimeMs);
            }
        }

        if (times.Count < 2)
        {
            dc.DrawText(Text("暂无帧时间数据", 13, TextSecondary), new Point(plot.Left + plot.Width / 2 - 48, plot.Top + plot.Height / 2 - 8));
            return;
        }

        // 纵轴量程：取 99 分位与 33.4ms 的较大值，避免个别尖峰压扁曲线
        var sorted = times.ToList();
        sorted.Sort();
        var p99 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(0.99 * sorted.Count) - 1)];
        var scale = Math.Max(33.4, p99 * 1.05);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < times.Count; i++)
            {
                var x = plot.Left + plot.Width * i / (times.Count - 1);
                var normalized = Math.Clamp(times[i] / scale, 0, 1);
                var y = plot.Bottom - plot.Height * normalized;
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Primary, 1.4) { LineJoin = PenLineJoin.Round }, geometry);

        // 1% Low 参考线
        if (stats.LowOnePercentPercentileMs > 0)
        {
            var lowY = plot.Bottom - plot.Height * Math.Clamp(stats.LowOnePercentPercentileMs / scale, 0, 1);
            dc.DrawLine(new Pen(Warning, 1) { DashStyle = DashStyles.Dash }, new Point(plot.Left, lowY), new Point(plot.Right, lowY));
            dc.DrawText(Text($"1% Low {stats.LowOnePercentPercentileMs.ToString("F1", CultureInfo.InvariantCulture)} ms", 10, Warning), new Point(plot.Left + 8, lowY - 13));
        }
    }

    private static FormattedText Text(string text, double size, Brush brush, bool bold = false)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            1.0);
    }
}
