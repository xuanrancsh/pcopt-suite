// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.ViewModels;

/// <summary>
/// 设置视图模型（T17，PRD A-P0-9 / 2.5 设置页）：开机自启（默认关，显式勾选）+ 版本信息。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AutoStartService _autoStart;

    /// <summary>初始化设置视图模型。</summary>
    public SettingsViewModel(AutoStartService autoStart)
    {
        _autoStart = autoStart ?? throw new ArgumentNullException(nameof(autoStart));
        // 直接写字段避免构造期触发 OnAutoStartEnabledChanged
        _autoStartEnabled = autoStart.IsEnabled;
        _versionText = $"GameMode Pilot {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0"} · pcopt-suite";
    }

    /// <summary>开机自启（默认关闭）。</summary>
    [ObservableProperty]
    private bool _autoStartEnabled;

    /// <summary>操作反馈文案。</summary>
    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>版本信息。</summary>
    [ObservableProperty]
    private string _versionText = string.Empty;

    /// <summary>勾选变化时写入 / 撤销开机自启注册项。</summary>
    partial void OnAutoStartEnabledChanged(bool value)
    {
        var result = _autoStart.SetEnabled(value);
        Message = result.IsSuccess
            ? (value ? "已开启开机自启（登录后自动在后台守护）" : "已关闭开机自启")
            : result.Error;
    }
}
