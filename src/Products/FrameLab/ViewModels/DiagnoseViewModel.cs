// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>
/// 诊断视图模型（T35 / T36，PRD C-P0-4 / C-P0-5）：
/// 结合帧时间统计与系统状态信号给出"症状 + 证据 + 建议 + 预期收益"，并可一键优化 → 引导复测。
/// </summary>
public sealed partial class DiagnoseViewModel : ObservableObject, IRefreshable
{
    private readonly CaptureSessionStore _store;
    private readonly ISystemSignalProvider _signals;
    private readonly OptimizationLoopService _optimization;
    private readonly INotificationService _notifications;

    /// <summary>初始化诊断视图模型。</summary>
    public DiagnoseViewModel(
        CaptureSessionStore store,
        ISystemSignalProvider signals,
        OptimizationLoopService optimization,
        INotificationService notifications)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _optimization = optimization ?? throw new ArgumentNullException(nameof(optimization));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        Refresh();
    }

    /// <summary>诊断结果行。</summary>
    public ObservableCollection<DiagnosisRowViewModel> Diagnoses { get; } = new();

    /// <summary>是否已有诊断结果（用于切换"空状态 / 列表"）。</summary>
    [ObservableProperty]
    private bool _hasDiagnoses;

    /// <summary>是否有采集数据可供诊断。</summary>
    [ObservableProperty]
    private bool _hasSession;

    /// <summary>无诊断项时的说明。</summary>
    [ObservableProperty]
    private string _emptyText = "还没有数据，先去「测试台」跑一次测试。";

    /// <summary>当前选中的诊断行。</summary>
    [ObservableProperty]
    private DiagnosisRowViewModel? _selectedDiagnosis;

    /// <summary>应用通道人话（产品 A 是否可用）。</summary>
    [ObservableProperty]
    private string _channelText = string.Empty;

    /// <summary>状态文案。</summary>
    [ObservableProperty]
    private string _statusText = "点「开始诊断」，看看这次卡顿的原因。";

    /// <summary>是否正在应用优化。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>刷新：重新读取系统信号与可用的应用通道。</summary>
    public void Refresh()
    {
        UpdateChannelText();
    }

    /// <summary>运行诊断（基于最近一次采集 + 当前系统信号）。</summary>
    [RelayCommand]
    private void RunDiagnosis()
    {
        var session = _store.Latest;
        if (session is null)
        {
            HasSession = false;
            Diagnoses.Clear();
            HasDiagnoses = false;
            EmptyText = "还没有数据，先去「测试台」跑一次测试。";
            StatusText = "暂无数据";
            return;
        }

        HasSession = true;

        var input = BuildInput(session);
        var results = Diagnoser.Diagnose(input);

        Diagnoses.Clear();
        foreach (var diagnosis in results)
        {
            Diagnoses.Add(new DiagnosisRowViewModel(diagnosis));
        }

        HasDiagnoses = Diagnoses.Count > 0;
        SelectedDiagnosis = Diagnoses.FirstOrDefault();

        if (HasDiagnoses)
        {
            StatusText = $"发现 {Diagnoses.Count} 个可能的原因，建议按顺序处理。";
            // 把当前成绩记作"优化前"基线，便于复测后自动对比（T36 闭环）
            _store.SetBaseline(session);
        }
        else
        {
            EmptyText = "这次表现不错，没有发现明显的性能问题。";
            StatusText = "未发现明显问题";
        }
    }

    /// <summary>应用诊断建议（优先产品 A，A 未运行时降级到内置最小引擎）并引导复测。</summary>
    [RelayCommand]
    private void ApplyOptimization()
    {
        if (IsBusy)
        {
            return;
        }

        if (Diagnoses.Count == 0)
        {
            _notifications.Notify("请先点「开始诊断」。");
            return;
        }

        IsBusy = true;
        try
        {
            var diagnoses = Diagnoses.Select(d => d.Model).ToArray();
            var result = _optimization.Apply(diagnoses);
            if (!result.IsSuccess)
            {
                StatusText = "应用优化失败";
                _notifications.Notify(result.Error);
                return;
            }

            var report = result.Value!;
            StatusText = $"已通过「{report.Channel}」应用优化，请回到「测试台」再测一次做对比。";
            _notifications.Notify(report.Message);
            UpdateChannelText();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>还原本次优化。</summary>
    [RelayCommand]
    private void RevertOptimization()
    {
        var result = _optimization.Revert();
        StatusText = result.IsSuccess ? result.Value! : "还原失败：" + result.Error;
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    private void UpdateChannelText()
    {
        ChannelText = _optimization.IsPilotAvailable
            ? "将使用「游戏模式（GameMode Pilot）」应用全部建议。"
            : "未检测到「游戏模式」，需要它的建议将无法自动应用；其余建议由内置最小引擎完成。";
    }

    private DiagnosisInput BuildInput(CaptureSession session)
    {
        var signals = _signals.Read();
        return new DiagnosisInput
        {
            Stats = session.Stats,
            StandbyListExpanded = signals.StandbyListExpanded,
            CpuThrottling = signals.CpuThrottling,
            GpuBottleneck = signals.GpuBottleneck || DetectGpuBottleneck(session),
            HagsEnabled = signals.HagsEnabled,
            MpoEnabled = signals.MpoEnabled,
            GpuVendor = signals.GpuVendor,
        };
    }

    /// <summary>
    /// 由采样数据推断是否 GPU 瓶颈：GPU 忙时的中位数长期贴着帧时间中位数（≥90%）。
    /// 采样不足或缺少 GPU 列时保守返回 false（不夸大，PRD 5.3）。
    /// </summary>
    private static bool DetectGpuBottleneck(CaptureSession session)
    {
        var gpuSamples = session.Samples
            .Where(s => s.GpuMs is > 0)
            .Select(s => s.GpuMs!.Value)
            .ToList();

        if (gpuSamples.Count < 10)
        {
            return false;
        }

        var medianFrame = Median(session.Samples.Select(s => s.FrameTimeMs).Where(v => v > 0).ToList());
        if (medianFrame <= 0)
        {
            return false;
        }

        var medianGpu = Median(gpuSamples);
        return medianGpu >= medianFrame * 0.9;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}
