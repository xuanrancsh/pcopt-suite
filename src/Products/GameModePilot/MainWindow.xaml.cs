// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using PcOpt.Products.GameModePilot.Services;
using PcOpt.Products.GameModePilot.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PcOpt.Products.GameModePilot;

/// <summary>
/// 产品 A 主窗口（T15）：承载导航（主界面 / 游戏 / 设置）+ 右下角 Toast 浮层 + 托盘常驻（T17）。
/// 关闭窗口 = 最小化到托盘（守护程序标准形态）；从托盘"退出"才真正结束进程。
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _mainViewModel;
    private readonly ProductTrayService _tray;
    private readonly ILogger<MainWindow> _logger;
    private bool _exiting;

    /// <summary>初始化主窗口。</summary>
    public MainWindow(
        MainViewModel mainViewModel,
        GamesViewModel gamesViewModel,
        SettingsViewModel settingsViewModel,
        ProductTrayService tray,
        ILogger<MainWindow> logger)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        InitializeComponent();

        DataContext = mainViewModel;
        GamesViewControl.DataContext = gamesViewModel ?? throw new ArgumentNullException(nameof(gamesViewModel));
        SettingsViewControl.DataContext = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

        _mainViewModel.ToastRequested += OnToastRequested;
        _tray.OpenRequested += OnTrayOpenRequested;
        _tray.ExitRequested += OnTrayExitRequested;
        _tray.PauseRequested += OnTrayPauseRequested;
        _tray.RevertAllRequested += OnTrayRevertAllRequested;

        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            // 关闭 = 收到托盘（守护常驻）
            e.Cancel = true;
            Hide();
            _tray.Notify("GameMode Pilot 仍在守护", "已在后台继续守护，双击托盘图标可重新打开。");
            return;
        }

        base.OnClosing(e);
    }

    private void OnToastRequested(object? sender, string message) =>
        Dispatcher.Invoke(() => Toasts.ShowToast(message));

    private void OnTrayOpenRequested(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    });

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        _exiting = true;
        Dispatcher.Invoke(() =>
        {
            _tray.Dispose();
            Application.Current?.Shutdown();
        });
    }

    private void OnTrayPauseRequested(object? sender, EventArgs e)
    {
        _logger.LogInformation("托盘请求：暂停守护 1 小时");
        Dispatcher.Invoke(() =>
        {
            // 暂停 1 小时（到期自动恢复），不改变守护总开关
            _mainViewModel.PauseGuardianFor(TimeSpan.FromHours(1));
            _tray.Notify("已暂停守护 1 小时", "1 小时内不再自动优化，到期后会自动恢复。");
        });
    }

    private void OnTrayRevertAllRequested(object? sender, EventArgs e)
    {
        _mainViewModel.RevertAllCommand.Execute(null);
        Dispatcher.Invoke(() => _tray.Notify("已执行全部还原", "所有改动已还原。"));
    }
}
