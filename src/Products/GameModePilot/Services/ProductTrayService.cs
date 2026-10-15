// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.UI.Services;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 产品 A 托盘服务（T17，PRD A-P0-9）。
/// 复用组件库 <see cref="TrayService"/>（NotifyIcon）；菜单随守护 / 会话状态动态刷新，
/// 悬停提示反映"守护中 / 游戏中"（游戏时图标提示变化）。
/// </summary>
public sealed class ProductTrayService : IDisposable
{
    private readonly TrayService _tray;
    private bool _disposed;

    /// <summary>初始化托盘服务。</summary>
    public ProductTrayService()
    {
        _tray = new TrayService("GameMode Pilot - 守护中");
        _tray.OpenRequested += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _tray.ExitRequested += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        RefreshMenu();
    }

    /// <summary>请求打开主界面（双击托盘 / 菜单）。</summary>
    public event EventHandler? OpenRequested;

    /// <summary>请求退出应用。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>请求暂停守护 1 小时。</summary>
    public event EventHandler? PauseRequested;

    /// <summary>请求全部还原。</summary>
    public event EventHandler? RevertAllRequested;

    /// <summary>更新悬停提示。</summary>
    public void UpdateToolTip(SessionSnapshot snapshot, bool guardianEnabled) =>
        _tray.SetToolTip(BuildToolTip(snapshot, guardianEnabled));

    /// <summary>显示气泡通知。</summary>
    public void Notify(string title, string message) => _tray.ShowBalloonTip(title, message);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tray.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RefreshMenu()
    {
        _tray.SetMenuItems(new[]
        {
            new TrayMenuItem { Text = "暂停守护 1 小时", OnClick = () => PauseRequested?.Invoke(this, EventArgs.Empty) },
            new TrayMenuItem { Text = "全部还原", OnClick = () => RevertAllRequested?.Invoke(this, EventArgs.Empty) },
            TrayMenuItem.Separator,
        });
    }

    private static string BuildToolTip(SessionSnapshot snapshot, bool guardianEnabled) => snapshot.Phase switch
    {
        SessionPhase.Playing => $"GameMode Pilot - 游戏中：{snapshot.GameName}",
        SessionPhase.Reverting => "GameMode Pilot - 正在还原",
        _ => guardianEnabled ? "GameMode Pilot - 守护中" : "GameMode Pilot - 已暂停守护",
    };
}
