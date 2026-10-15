// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>
/// 测试台视图模型（T31/T32，PRD 4.5/4.6）：开始测试（默认 60 秒，时长可配）+ 实时迷你图 + 最近成绩。
/// 采集在后台线程执行，实时帧样本经 <see cref="Progress{T}"/> 回主线程刷新迷你图。
/// </summary>
public sealed partial class TestBenchViewModel : ObservableObject, IRefreshable
{
    /// <summary>实时迷你图刷新节流：每累计 N 帧刷新一次（避免逐帧刷 UI）。</summary>
    private const int LiveRefreshEveryNFrames = 25;

    private readonly CaptureService _capture;
    private readonly CaptureSessionStore _store;
    private readonly HistoryService _history;
    private readonly INotificationService _notifications;
    private readonly SettingsService _settings;
    private readonly ILogger<TestBenchViewModel> _logger;

    private readonly List<double> _liveBuffer = new();
    private CancellationTokenSource? _cts;

    /// <summary>初始化测试台。</summary>
    public TestBenchViewModel(
        CaptureService capture,
        CaptureSessionStore store,
        HistoryService history,
        INotificationService notifications,
        SettingsService settings,
        ILogger<TestBenchViewModel> logger)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        DurationSeconds = _settings.Current.DurationSeconds;
        ProcessName = _settings.Current.LastProcessName;
        Refresh();
    }

    /// <summary>是否正在采集。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>状态文案。</summary>
    [ObservableProperty]
    private string _statusText = "准备就绪";

    /// <summary>采集时长（秒）。</summary>
    [ObservableProperty]
    private int _durationSeconds = 60;

    /// <summary>目标进程名（可空：采集全屏前台）。</summary>
    [ObservableProperty]
    private string _processName = string.Empty;

    /// <summary>实时帧时间（毫秒；驱动迷你图）。</summary>
    [ObservableProperty]
    private IReadOnlyList<double> _liveFrameTimes = Array.Empty<double>();

    /// <summary>是否已有成绩。</summary>
    [ObservableProperty]
    private bool _hasResult;

    /// <summary>最近成绩摘要。</summary>
    [ObservableProperty]
    private string _lastScoreText = "还没有成绩，点上面的按钮测一测吧";

    /// <summary>PresentMon 是否可用。</summary>
    [ObservableProperty]
    private bool _presentMonAvailable;

    /// <summary>PresentMon 不可用时的获取提示（可用时为 null）。</summary>
    [ObservableProperty]
    private string? _availabilityMessage;

    /// <summary>开始按钮文案（含时长）。</summary>
    public string StartButtonText => $"开始 {DurationSeconds} 秒测试";

    partial void OnDurationSecondsChanged(int value) => OnPropertyChanged(nameof(StartButtonText));

    /// <summary>刷新 PresentMon 可用性（导航切换 / 设置变更时调用）。</summary>
    public void Refresh()
    {
        var availability = _capture.Availability;
        PresentMonAvailable = availability.Available;
        AvailabilityMessage = availability.Available ? null : availability.Message;
    }

    /// <summary>开始一次采集（后台执行 + 实时迷你图）。</summary>
    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var availability = _capture.Availability;
        if (!availability.Available)
        {
            // 优雅降级：明确提示获取方式，不崩溃
            StatusText = "无法开始：缺少采集组件";
            _notifications.Notify(availability.Message);
            return;
        }

        if (DurationSeconds <= 0)
        {
            _notifications.Notify("测试时长需要大于 0 秒");
            return;
        }

        IsBusy = true;
        _liveBuffer.Clear();
        LiveFrameTimes = Array.Empty<double>();
        StatusText = $"正在采集，请保持游戏运行（{DurationSeconds} 秒）…";

        _settings.Current.DurationSeconds = DurationSeconds;
        _settings.Current.LastProcessName = ProcessName;
        _settings.Save();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<FrameSample>(OnSample);

        try
        {
            var process = ProcessName;
            var seconds = DurationSeconds;
            var result = await Task.Run(
                () => _capture.RunCapture(process, seconds, "本次测试", progress, token),
                token).ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusText = "测试未完成";
                _notifications.Notify(result.Error);
                return;
            }

            var session = result.Value!;
            _store.SetLatest(session);
            _history.Add(session);
            HasResult = true;
            LastScoreText = $"平均 {session.Stats.AverageFps.ToString("F0", CultureInfo.InvariantCulture)} FPS · " +
                $"1% Low {session.Stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile).ToString("F0", CultureInfo.InvariantCulture)}";
            StatusText = "测试完成";

            TryCompareBaseline(session);
            _notifications.Notify(
                $"测完了：平均 {session.Stats.AverageFps.ToString("F0", CultureInfo.InvariantCulture)} FPS，" +
                $"1% Low {session.Stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile).ToString("F0", CultureInfo.InvariantCulture)}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消采集";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            StatusText = "测试失败";
            _logger.LogWarning("采集异常：{Error}", ex.Message);
            _notifications.Notify($"测试失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>取消当前采集。</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsBusy)
        {
            _cts?.Cancel();
        }
    }

    private void OnSample(FrameSample sample)
    {
        _liveBuffer.Add(sample.FrameTimeMs);
        if (_liveBuffer.Count % LiveRefreshEveryNFrames == 0)
        {
            LiveFrameTimes = _liveBuffer.ToArray();
        }
    }

    /// <summary>若存在基线（优化前）会话，则自动生成前后对比（T36 闭环）。</summary>
    private void TryCompareBaseline(CaptureSession session)
    {
        var baseline = _store.Baseline;
        if (baseline is null || string.Equals(baseline.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return;
        }

        var comparison = ComparisonAnalyzer.Compare(baseline.Stats, session.Stats);
        _store.SetComparison(comparison);
        _notifications.Notify("已生成优化前后对比：" + comparison.VerdictText);
    }
}
