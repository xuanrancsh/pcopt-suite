// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using PcOpt.Products.FrameLab.Services;
using PcOpt.Products.FrameLab.ViewModels;
using PcOpt.UI.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.Products.FrameLab;

/// <summary>
/// 产品 C 主窗口（T31）：左侧导航（测试台 / 报告 / 诊断 / 历史 / 设置）+ 右侧内容区 + 右下角 Toast 浮层。
/// 退出时若内置最小引擎的优化仍在生效，按套件零残留承诺提示"是否还原"。
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _mainViewModel;
    private readonly OptimizationLoopService _optimization;
    private readonly INotificationService _notifications;
    private readonly ILogger<MainWindow> _logger;
    private bool _exiting;
    private bool _themeComboReady;

    /// <summary>初始化主窗口。</summary>
    public MainWindow(
        MainViewModel mainViewModel,
        OptimizationLoopService optimization,
        INotificationService notifications,
        ILogger<MainWindow> logger)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _optimization = optimization ?? throw new ArgumentNullException(nameof(optimization));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeComponent();

        DataContext = _mainViewModel;
        _themeComboReady = true;

        _notifications.ToastRequested += OnToastRequested;

        // 跟随系统深浅色（Mica 背景；不支持的系统自动降级）
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_exiting)
        {
            base.OnClosing(e);
            return;
        }

        // 仅在内置最小引擎仍生效、且产品 A 未接管时提示（零残留承诺）
        if (!_optimization.IsPilotAvailable && _optimization.IsMinimalApplied)
        {
            var decision = ConfirmDialog.Ask(
                this,
                "有临时优化仍在生效",
                "检测到内置优化（高性能电源 / 计时器）还在生效。退出前是否还原？",
                new[] { "选择“保留”则会继续保持生效", "选择“全部还原”会把电源计划等恢复到改动前", "之后也可在“诊断”页手动还原" });

            if (decision.Confirmed)
            {
                var revert = _optimization.Revert();
                if (!revert.IsSuccess)
                {
                    _notifications.Notify($"还原未完成：{revert.Error}");
                    e.Cancel = true;
                    return;
                }

                _notifications.Notify("已还原临时优化，你的电脑回到改动前的状态。");
            }
        }

        _exiting = true;
        base.OnClosing(e);
    }

    private void OnToastRequested(object? sender, string message) =>
        Dispatcher.Invoke(() => Toasts.ShowToast(message));

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeComboReady)
        {
            return;
        }

        var mode = ((ComboBox)sender).SelectedIndex switch
        {
            1 => PcOptThemeMode.Light,
            2 => PcOptThemeMode.Dark,
            _ => PcOptThemeMode.System,
        };

        try
        {
            App.ThemeService.Apply(mode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("主题切换失败：{Message}", ex.Message);
        }
    }
}
