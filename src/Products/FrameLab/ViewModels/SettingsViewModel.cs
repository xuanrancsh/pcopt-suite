// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>报告中 1% Low 口径的可选项（下拉框数据源）。</summary>
/// <param name="Text">人话名。</param>
/// <param name="Value">算法枚举值。</param>
public sealed record AlgorithmOption(string Text, LowOnePercentAlgorithm Value);

/// <summary>
/// 设置视图模型（T31）：默认采集时长、默认目标进程、报告中 1% Low 口径、温度曲线开关，
/// 并展示采集组件的可用性（未安装时给出获取指引，不崩溃）。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IRefreshable
{
    private readonly SettingsService _settings;
    private readonly CaptureService _capture;
    private readonly INotificationService _notifications;

    /// <summary>初始化设置视图模型。</summary>
    public SettingsViewModel(
        SettingsService settings,
        CaptureService capture,
        INotificationService notifications)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        AlgorithmOptions = new[]
        {
            new AlgorithmOption("统计百分位法（最慢 1% 的单帧水平）", LowOnePercentAlgorithm.Percentile),
            new AlgorithmOption("时间占比法（最慢 1% 时长的平均帧时间）", LowOnePercentAlgorithm.TimeBased),
        };

        LoadFromSettings();
        Refresh();
    }

    /// <summary>可选的 1% Low 口径。</summary>
    public IReadOnlyList<AlgorithmOption> AlgorithmOptions { get; }

    /// <summary>默认采集时长（秒）。</summary>
    [ObservableProperty]
    private int _durationSeconds = 60;

    /// <summary>默认目标进程名。</summary>
    [ObservableProperty]
    private string _lastProcessName = string.Empty;

    /// <summary>选中的 1% Low 口径。</summary>
    [ObservableProperty]
    private AlgorithmOption? _selectedAlgorithm;

    /// <summary>是否在报告中展示温度 / 功耗曲线。</summary>
    [ObservableProperty]
    private bool _showThermal = true;

    /// <summary>采集组件（PresentMon）是否可用。</summary>
    [ObservableProperty]
    private bool _presentMonAvailable;

    /// <summary>采集组件可用性说明（不可用时含获取方式）。</summary>
    [ObservableProperty]
    private string _presentMonStatus = string.Empty;

    /// <summary>刷新采集组件可用性。</summary>
    public void Refresh()
    {
        var availability = _capture.Availability;
        PresentMonAvailable = availability.Available;
        PresentMonStatus = availability.Available
            ? "采集组件已就绪：" + availability.ExecutablePath
            : availability.Message;
    }

    /// <summary>保存设置。</summary>
    [RelayCommand]
    private void Save()
    {
        if (DurationSeconds <= 0)
        {
            _notifications.Notify("默认测试时长需要大于 0 秒。");
            return;
        }

        _settings.Current.DurationSeconds = DurationSeconds;
        _settings.Current.LastProcessName = LastProcessName ?? string.Empty;
        _settings.Current.PreferredAlgorithm = SelectedAlgorithm?.Value ?? LowOnePercentAlgorithm.Percentile;
        _settings.Current.ShowThermal = ShowThermal;
        _settings.Save();

        _notifications.Notify("设置已保存。");
    }

    private void LoadFromSettings()
    {
        var current = _settings.Current;
        DurationSeconds = current.DurationSeconds;
        LastProcessName = current.LastProcessName;
        ShowThermal = current.ShowThermal;
        SelectedAlgorithm = AlgorithmOptions.FirstOrDefault(o => o.Value == current.PreferredAlgorithm)
            ?? AlgorithmOptions[0];
    }
}
