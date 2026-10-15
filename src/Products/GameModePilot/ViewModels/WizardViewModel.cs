// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.ViewModels;

/// <summary>
/// 首次运行向导视图模型（T16，PRD A-P0-7）：3 步 —— ① 说明会改什么 ② 扫描游戏与硬件 ③ 推荐默认档位。
/// 每步可跳过、可回退；开机自启默认不勾选（PRD 明确默认关）。
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private readonly GameWatcher _watcher;
    private readonly AutoStartService _autoStart;
    private readonly ILogger<WizardViewModel> _logger;

    private bool _initialized;

    /// <summary>初始化向导视图模型。</summary>
    public WizardViewModel(GameWatcher watcher, AutoStartService autoStart, ILogger<WizardViewModel> logger)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>向导完成事件（视图据此关闭向导并进入主界面）。</summary>
    public event EventHandler? Completed;

    /// <summary>步骤标签（PRD 5.4 ProgressSteps）。</summary>
    public IReadOnlyList<string> Steps { get; } = new[] { "了解会改什么", "扫描游戏与硬件", "选择默认档位" };

    /// <summary>当前步骤（0 起）。</summary>
    [ObservableProperty]
    private int _currentStep;

    /// <summary>已识别游戏数。</summary>
    [ObservableProperty]
    private int _gameCount;

    /// <summary>是否正在扫描。</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>扫描状态文案。</summary>
    [ObservableProperty]
    private string _scanStatus = "点下一步即可自动扫描你已安装的游戏与硬件配置";

    /// <summary>开机自启（默认不勾选，PRD 明确默认关）。</summary>
    [ObservableProperty]
    private bool _autoStartEnabled;

    /// <summary>当前步骤标题。</summary>
    public string CurrentStepTitle => Steps[Math.Clamp(CurrentStep, 0, Steps.Count - 1)];

    partial void OnCurrentStepChanged(int value) => OnPropertyChanged(nameof(CurrentStepTitle));

    /// <summary>下一步；末步完成向导。</summary>
    [RelayCommand]
    private void Next()
    {
        if (CurrentStep >= Steps.Count - 1)
        {
            Complete();
            return;
        }

        CurrentStep++;
    }

    /// <summary>上一步。</summary>
    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    /// <summary>跳过向导（直接进入主界面，不改变任何设置）。</summary>
    [RelayCommand]
    private void Skip() => Complete();

    /// <summary>扫描游戏库（步骤②）。</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ScanStatus = "正在扫描游戏库…";
        try
        {
            var result = await _watcher.ScanLibraryAsync().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                ScanStatus = $"扫描失败：{result.Error}";
                return;
            }

            GameCount = result.Value!.Games.Count;
            ScanStatus = $"扫描完成：共识别 {GameCount} 款游戏，已为它们准备好自动优化";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void Complete()
    {
        _initialized = true;

        // 仅在用户显式勾选时才开启开机自启（默认关）
        if (AutoStartEnabled)
        {
            var result = _autoStart.SetEnabled(true);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("向导开启开机自启失败：{Error}", result.Error);
            }
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>是否已走完向导（供调用方判断）。</summary>
    public bool IsInitialized => _initialized;
}
