// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using PcOpt.Products.GameOptBox.Services;
using PcOpt.Products.GameOptBox.ViewModels;
using PcOpt.UI.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.Products.GameOptBox;

/// <summary>
/// 产品 B 主窗口（T20）：左侧导航（11 模块）+ 右侧内容区 + 右下角 Toast 浮层。
/// 退出时若有 N 项优化生效，按 PRD 3.6 提示"是否保留或全部还原"（合规零残留承诺）。
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _mainViewModel;
    private readonly RuleRepository _rules;
    private readonly INotificationService _notifications;
    private readonly ILogger<MainWindow> _logger;
    private bool _exiting;
    private bool _themeComboReady;

    /// <summary>初始化主窗口。</summary>
    public MainWindow(
        MainViewModel mainViewModel,
        RuleRepository rules,
        INotificationService notifications,
        ILogger<MainWindow> logger)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
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

        var applied = _rules.CountApplied();
        if (applied <= 0)
        {
            _exiting = true;
            base.OnClosing(e);
            return;
        }

        // 有 N 项优化生效：询问保留还是全部还原（PRD 3.6 / 5.4）
        var decision = ConfirmDialog.Ask(
            this,
            "有优化项正在生效",
            $"当前有 {applied} 项优化生效。退出前是否把它们全部还原？",
            new[] { "选择“保留”则这些改动会一直生效", "选择“全部还原”会把电脑恢复到改动前的状态", "之后也可在“还原中心”手动还原" });

        if (decision.Confirmed)
        {
            var revert = _rules.RevertAll();
            if (!revert.IsSuccess)
            {
                // 还原失败：保留窗口（不退出），提示用户稍后重试
                _notifications.Notify($"还原未完成：{revert.Error}");
                e.Cancel = true;
                return;
            }

            _notifications.Notify($"已全部还原，共 {revert.Value!.RevertedEntries} 项改动");
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
