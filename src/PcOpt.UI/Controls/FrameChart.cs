// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PcOpt.UI.Controls;

/// <summary>
/// 产品 C 的帧时间图（PRD 5.6 P0，图表方案：WPF 原生自绘，不引入第三方图表包）。
/// 自绘卡片 + 网格 + 帧时间折线 + 均值虚线 + 1% Low 标记；每次绘制经 TryFindResource
/// 动态解析主题 Token，深浅色切换无需重建（配合 <see cref="Services.PcOptThemeService"/> 主题事件重绘）。
/// </summary>
public class FrameChart : Control
{
    /// <summary>帧时间数据（毫秒，有序）。</summary>
    public static readonly DependencyProperty FrameTimesProperty = DependencyProperty.Register(
        nameof(FrameTimes), typeof(IEnumerable), typeof(FrameChart), new PropertyMetadata(null, OnDataChanged));

    /// <summary>平均帧时间（毫秒；null 时不画均值线）。</summary>
    public static readonly DependencyProperty AverageMsProperty = DependencyProperty.Register(
        nameof(AverageMs), typeof(double?), typeof(FrameChart), new PropertyMetadata(null, OnDataChanged));

    /// <summary>1% Low 帧时间（毫秒；null 时不画标记）。</summary>
    public static readonly DependencyProperty LowOnePercentMsProperty = DependencyProperty.Register(
        nameof(LowOnePercentMs), typeof(double?), typeof(FrameChart), new PropertyMetadata(null, OnDataChanged));

    /// <summary>图表标题。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FrameChart), new PropertyMetadata("帧时间"));

    /// <summary>纵轴量程（毫秒；默认 33.4 ≈ 30FPS 帧时间）。</summary>
    public static readonly DependencyProperty ScaleMsProperty = DependencyProperty.Register(
        nameof(ScaleMs), typeof(double), typeof(FrameChart), new PropertyMetadata(33.4, OnDataChanged));

    /// <summary>初始化图表实例并订阅主题切换事件（退订防泄漏）。</summary>
    public FrameChart()
    {
        Services.PcOptThemeService.GlobalThemeChanged += OnThemeChanged;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Services.PcOptThemeService.GlobalThemeChanged -= OnThemeChanged;
        Unloaded -= OnUnloaded;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // 深浅色切换后立即重绘（Token 经 TryFindResource 动态解析，此处只触发重绘）
        Dispatcher.Invoke(InvalidateVisual);
    }

    /// <summary>帧时间数据（毫秒）。</summary>
    public IEnumerable? FrameTimes
    {
        get => (IEnumerable?)GetValue(FrameTimesProperty);
        set => SetValue(FrameTimesProperty, value);
    }

    /// <summary>平均帧时间（毫秒）。</summary>
    public double? AverageMs
    {
        get => (double?)GetValue(AverageMsProperty);
        set => SetValue(AverageMsProperty, value);
    }

    /// <summary>1% Low 帧时间（毫秒）。</summary>
    public double? LowOnePercentMs
    {
        get => (double?)GetValue(LowOnePercentMsProperty);
        set => SetValue(LowOnePercentMsProperty, value);
    }

    /// <summary>图表标题。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>纵轴量程（毫秒）。</summary>
    public double ScaleMs
    {
        get => (double)GetValue(ScaleMsProperty);
        set => SetValue(ScaleMsProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (rect.Width < 8 || rect.Height < 8)
        {
            return;
        }

        // 1) 卡片底 + 边框（圆角 8 = 卡片 Token）
        var cardBrush = GetBrush("PcOpt.Brush.CardBackground");
        var borderBrush = GetBrush("PcOpt.Brush.CardBorder");
        var card = new RectangleGeometry(new Rect(0, 0, rect.Width, rect.Height), 8, 8);
        dc.DrawGeometry(cardBrush, new Pen(borderBrush, 1) { LineJoin = PenLineJoin.Round }, card);

        const double padding = 12;
        var titleHeight = 24.0;
        var plot = new Rect(padding, padding + titleHeight, rect.Width - padding * 2, rect.Height - padding * 2 - titleHeight);
        if (plot.Width < 20 || plot.Height < 20)
        {
            return;
        }

        // 2) 标题 + 图例
        var textBrush = GetBrush("PcOpt.Brush.TextPrimary");
        var secondaryBrush = GetBrush("PcOpt.Brush.TextSecondary");
        var title = CreateText(Title, 13, FontWeights.SemiBold, textBrush, dpi);
        dc.DrawText(title, new Point(padding, padding));

        var scale = ScaleMs > 0 ? ScaleMs : 33.4;

        // 3) 水平网格线（4 条）
        var gridBrush = GetBrush("PcOpt.Brush.ChartGrid");
        var gridPen = new Pen(gridBrush, 1) { DashStyle = DashStyles.Dot };
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        // 纵轴刻度（0 / 半程 / 满程）
        dc.DrawText(CreateText("0 ms", 10, FontWeights.Normal, secondaryBrush, dpi), new Point(plot.Left, plot.Bottom + 2));
        dc.DrawText(
            CreateText($"{scale.ToString("F1", CultureInfo.InvariantCulture)} ms", 10, FontWeights.Normal, secondaryBrush, dpi),
            new Point(plot.Left, plot.Top - 12));

        // 4) 帧时间折线（电竞蓝，超出量程钳顶）
        var samples = ReadSamples();
        if (samples.Count >= 2)
        {
            var linePen = new Pen(GetBrush("PcOpt.Brush.Primary.Solid"), 1.5) { LineJoin = PenLineJoin.Round };
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (var i = 0; i < samples.Count; i++)
                {
                    var x = plot.Left + plot.Width * i / (samples.Count - 1);
                    var normalized = Math.Clamp(samples[i] / scale, 0, 1);
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
            dc.DrawGeometry(null, linePen, geometry);
        }

        // 5) 均值虚线
        if (AverageMs is { } average && average > 0)
        {
            var averageY = plot.Bottom - plot.Height * Math.Clamp(average / scale, 0, 1);
            var averagePen = new Pen(GetBrush("PcOpt.Brush.ChartAverage"), 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(averagePen, new Point(plot.Left, averageY), new Point(plot.Right, averageY));
            dc.DrawText(
                CreateText($"平均 {average.ToString("F1", CultureInfo.InvariantCulture)} ms", 10, FontWeights.Normal, secondaryBrush, dpi),
                new Point(plot.Right - 88, averageY - 13));
        }

        // 6) 1% Low 标记（琥珀点）
        if (LowOnePercentMs is { } low && low > 0)
        {
            var lowY = plot.Bottom - plot.Height * Math.Clamp(low / scale, 0, 1);
            dc.DrawEllipse(
                GetBrush("PcOpt.Brush.Processing"),
                null,
                new Point(plot.Left + 6, lowY),
                4,
                4);
            dc.DrawText(
                CreateText($"1% Low {low.ToString("F1", CultureInfo.InvariantCulture)} ms", 10, FontWeights.Normal, GetBrush("PcOpt.Brush.Processing"), dpi),
                new Point(plot.Left + 14, lowY - 6));
        }

        // 7) 空数据提示
        if (samples.Count < 2)
        {
            dc.DrawText(
                CreateText("暂无帧时间数据", 11, FontWeights.Normal, secondaryBrush, dpi),
                new Point(plot.Left + plot.Width / 2 - 44, plot.Top + plot.Height / 2 - 8));
        }
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((FrameChart)sender).InvalidateVisual();
    }

    private List<double> ReadSamples()
    {
        var result = new List<double>();
        if (FrameTimes is null)
        {
            return result;
        }

        foreach (var item in FrameTimes)
        {
            if (item is double value && value > 0 && !double.IsNaN(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private Brush GetBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private FormattedText CreateText(string text, double size, FontWeight weight, Brush brush, DpiScale dpi)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            dpi.PixelsPerDip);
        return formatted;
    }
}
