// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.FrameLab.Services;
using PcOpt.UI.Controls;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>
/// 报告视图模型（T34，PRD C-P0-3 / C-P0-6）：
/// 帧时间图 + 关键指标（1% Low 双算法并标注口径）+ 优化前后对比表 + CSV / PNG 导出。
/// </summary>
public sealed partial class ReportViewModel : ObservableObject, IRefreshable
{
    private readonly CaptureSessionStore _store;
    private readonly SettingsService _settings;
    private readonly ReportExporter _exporter;
    private readonly INotificationService _notifications;

    /// <summary>初始化报告视图模型。</summary>
    public ReportViewModel(
        CaptureSessionStore store,
        SettingsService settings,
        ReportExporter exporter,
        INotificationService notifications)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        Refresh();
    }

    /// <summary>是否已有可展示的采集数据。</summary>
    [ObservableProperty]
    private bool _hasData;

    /// <summary>是否存在优化前后对比。</summary>
    [ObservableProperty]
    private bool _hasComparison;

    /// <summary>帧时间序列（毫秒，驱动帧时间图）。</summary>
    [ObservableProperty]
    private IReadOnlyList<double> _frameTimes = Array.Empty<double>();

    /// <summary>平均帧时间（毫秒；图表均值线）。</summary>
    [ObservableProperty]
    private double? _averageMs;

    /// <summary>1% Low 帧时间（毫秒；图表 1% Low 标记，取当前偏好口径）。</summary>
    [ObservableProperty]
    private double? _lowOnePercentMs;

    /// <summary>帧时间图标题。</summary>
    [ObservableProperty]
    private string _chartTitle = "帧时间（毫秒）";

    /// <summary>会话摘要（时间 / 进程 / 时长）。</summary>
    [ObservableProperty]
    private string _sessionInfo = "还没有采集数据，先去「测试台」跑一次测试吧";

    /// <summary>平均帧率展示值。</summary>
    [ObservableProperty]
    private string _averageFpsText = "—";

    /// <summary>平均帧时间展示值。</summary>
    [ObservableProperty]
    private string _averageFrameTimeText = "—";

    /// <summary>中位帧时间展示值。</summary>
    [ObservableProperty]
    private string _medianFrameTimeText = "—";

    /// <summary>1% Low（时间占比法）展示值。</summary>
    [ObservableProperty]
    private string _lowOnePercentTimeBasedText = "—";

    /// <summary>1% Low（统计百分位法）展示值。</summary>
    [ObservableProperty]
    private string _lowOnePercentPercentileText = "—";

    /// <summary>卡顿次数展示值。</summary>
    [ObservableProperty]
    private string _stutterText = "—";

    /// <summary>DPC / 中断延迟展示值（近似口径）。</summary>
    [ObservableProperty]
    private string _dpcText = "—";

    /// <summary>帧数展示值。</summary>
    [ObservableProperty]
    private string _frameCountText = "—";

    /// <summary>当前偏好口径人话（设置页可切换）。</summary>
    [ObservableProperty]
    private string _preferredAlgorithmText = "1% Low 口径：统计百分位法";

    /// <summary>1% Low 双算法 + HAGS 口径说明（PRD 4.7-3，强制展示）。</summary>
    [ObservableProperty]
    private string _algorithmNote = ComparisonAnalyzer.AlgorithmAnnotation;

    /// <summary>前后对比表数据行。</summary>
    [ObservableProperty]
    private IEnumerable<MetricComparison> _comparisonRows = Array.Empty<MetricComparison>();

    /// <summary>前后对比整体结论人话。</summary>
    [ObservableProperty]
    private string _comparisonConclusion = string.Empty;

    /// <summary>前后对比一句话总结。</summary>
    [ObservableProperty]
    private string _comparisonSummary = string.Empty;

    /// <summary>刷新报告（导航切换 / 采集完成时调用）。</summary>
    public void Refresh()
    {
        var session = _store.Latest;
        if (session is null)
        {
            HasData = false;
            HasComparison = _store.Comparison is not null;
            ApplyComparison(_store.Comparison);
            return;
        }

        HasData = true;
        var stats = session.Stats;
        var algorithm = _settings.Current.PreferredAlgorithm;

        FrameTimes = session.Samples.Select(s => s.FrameTimeMs).ToArray();
        AverageMs = stats.AverageFrameTimeMs > 0 ? stats.AverageFrameTimeMs : null;
        LowOnePercentMs = stats.LowOnePercentFrameTimeMs(algorithm) > 0
            ? stats.LowOnePercentFrameTimeMs(algorithm)
            : null;
        ChartTitle = "帧时间（毫秒）· 1% Low 口径：" + AlgorithmName(algorithm);
        SessionInfo = BuildSessionInfo(session);

        AverageFpsText = Fps(stats.AverageFps);
        AverageFrameTimeText = Milli(stats.AverageFrameTimeMs);
        MedianFrameTimeText = Milli(stats.MedianFrameTimeMs);
        LowOnePercentTimeBasedText = Fps(stats.LowOnePercentFps(LowOnePercentAlgorithm.TimeBased));
        LowOnePercentPercentileText = Fps(stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile));
        StutterText = stats.StutterCount.ToString(CultureInfo.InvariantCulture) + " 次";
        DpcText = stats.Dpc.Samples > 0
            ? $"平均 {Milli(stats.Dpc.AverageMs)} / 最高 {Milli(stats.Dpc.MaxMs)}"
            : "未采样";
        FrameCountText = stats.FrameCount.ToString(CultureInfo.InvariantCulture) + " 帧";
        PreferredAlgorithmText = "1% Low 口径：" + AlgorithmName(algorithm);

        ApplyComparison(_store.Comparison);
    }

    /// <summary>导出帧时间明细 CSV。</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        var session = _store.Latest;
        if (session is null)
        {
            _notifications.Notify("还没有数据可导出，先跑一次测试。");
            return;
        }

        var path = Path.Combine(ResourcePaths.ExportsDirectory, $"FrameLab-帧时间-{Stamp()}.csv");
        var result = _exporter.ExportCsv(session.Stats, session.Samples, path);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    /// <summary>导出帧时间报告图 PNG。</summary>
    [RelayCommand]
    private void ExportPng()
    {
        var session = _store.Latest;
        if (session is null)
        {
            _notifications.Notify("还没有数据可导出，先跑一次测试。");
            return;
        }

        var path = Path.Combine(ResourcePaths.ExportsDirectory, $"FrameLab-报告图-{Stamp()}.png");
        var result = _exporter.ExportPng(session.Stats, session.Samples, path);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    /// <summary>导出优化前后对比 CSV。</summary>
    [RelayCommand]
    private void ExportComparisonCsv()
    {
        var comparison = _store.Comparison;
        if (comparison is null)
        {
            _notifications.Notify("还没有可对比的数据：先测一次基线，优化后再测一次。");
            return;
        }

        var path = Path.Combine(ResourcePaths.ExportsDirectory, $"FrameLab-对比-{Stamp()}.csv");
        var result = _exporter.ExportComparisonCsv(comparison, path);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    private void ApplyComparison(ComparisonResult? comparison)
    {
        if (comparison is null)
        {
            HasComparison = false;
            ComparisonRows = Array.Empty<MetricComparison>();
            ComparisonConclusion = string.Empty;
            ComparisonSummary = string.Empty;
            return;
        }

        HasComparison = true;
        ComparisonConclusion = comparison.VerdictText;
        ComparisonSummary = comparison.Summary;
        AlgorithmNote = comparison.AlgorithmNote;
        ComparisonRows = comparison.Rows
            .Select(r => new MetricComparison(r.Name, r.BeforeText, r.AfterText, r.DeltaText, MapVerdict(r.Verdict)))
            .ToArray();
    }

    private static MetricVerdict MapVerdict(ComparisonRowVerdict verdict) => verdict switch
    {
        ComparisonRowVerdict.Better => MetricVerdict.Better,
        ComparisonRowVerdict.Worse => MetricVerdict.Worse,
        _ => MetricVerdict.Same,
    };

    private static string BuildSessionInfo(CaptureSession session)
    {
        var process = string.IsNullOrWhiteSpace(session.ProcessName) ? "（未指定进程）" : session.ProcessName;
        var label = string.IsNullOrWhiteSpace(session.Label) ? "本次测试" : session.Label;
        return $"{label} · {process} · {session.DurationSeconds} 秒 · " +
            session.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>1% Low 口径人话名。</summary>
    public static string AlgorithmName(LowOnePercentAlgorithm algorithm) =>
        algorithm == LowOnePercentAlgorithm.TimeBased ? "时间占比法" : "统计百分位法";

    private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static string Fps(double value) => value.ToString("F0", CultureInfo.InvariantCulture) + " FPS";

    private static string Milli(double value) => value.ToString("F2", CultureInfo.InvariantCulture) + " ms";
}
