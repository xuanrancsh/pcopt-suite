// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Drawing;
using System.Windows.Forms;

namespace PcOpt.UI.Services;

/// <summary>托盘菜单项。</summary>
public sealed class TrayMenuItem
{
    /// <summary>菜单文案（人话）。</summary>
    public required string Text { get; init; }

    /// <summary>点击回调；为 null 时该项渲染为分隔线（见 <see cref="Separator"/>）。</summary>
    public Action? OnClick { get; init; }

    /// <summary>分隔线菜单项（点击回调为 null 即渲染为分隔线）。</summary>
    public static TrayMenuItem Separator => new() { Text = "-", OnClick = null };
}

/// <summary>
/// 托盘服务（PRD 5.6 P0，三产品共用）：托盘图标 + 菜单 + 气泡通知。
/// 实现：System.Windows.Forms.NotifyIcon（WPF 应用标准做法，随 UseWindowsForms 引入）。
/// 图标可注入（产品传自己的 .ico）；未提供时用系统应用图标占位（Demo / 骨架阶段）。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private bool _disposed;

    /// <summary>初始化托盘服务。</summary>
    /// <param name="toolTip">悬停提示（如"GameModePilot - 守护中"）。</param>
    /// <param name="icon">托盘图标；null 用系统应用图标。</param>
    public TrayService(string toolTip = "pcopt-suite", Icon? icon = null)
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = Truncate(toolTip, 63), // NotifyIcon.Text 上限 63 字符
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
    }

    /// <summary>双击图标 / 点击"打开主界面"时触发。</summary>
    public event EventHandler? OpenRequested;

    /// <summary>点击"退出"时触发。</summary>
    public event EventHandler? ExitRequested;

    /// <summary>当前悬停提示。</summary>
    public string ToolTip => _notifyIcon.Text;

    /// <summary>更新悬停提示（超长截断，防异常）。</summary>
    public void SetToolTip(string text) => _notifyIcon.Text = Truncate(text, 63);

    /// <summary>重建托盘菜单（含固定尾项：打开主界面 / 退出）。</summary>
    public void SetMenuItems(IEnumerable<TrayMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _menu.Items.Clear();
        foreach (var item in items)
        {
            if (item.OnClick is null)
            {
                // 分隔线
                _menu.Items.Add(new ToolStripSeparator());
                continue;
            }

            var menuItem = new ToolStripMenuItem(item.Text);
            var callback = item.OnClick;
            menuItem.Click += (_, _) => callback();
            _menu.Items.Add(menuItem);
        }

        _menu.Items.Add(new ToolStripSeparator());
        var openItem = new ToolStripMenuItem("打开主界面");
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(openItem);

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exitItem);
    }

    /// <summary>显示气泡通知（3 秒）。</summary>
    public void ShowBalloonTip(string title, string message)
    {
        _notifyIcon.ShowBalloonTip(3000, Truncate(title, 63), Truncate(message, 255), ToolTipIcon.Info);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.DoubleClick -= OnDoubleClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnDoubleClick(object? sender, EventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max];
}
