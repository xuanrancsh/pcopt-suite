// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using System.Windows.Controls;
// 与组件库主题服务同一枚举（PcOpt.UI.Services.ThemeMode）；用别名避免同名类型歧义
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.Products.GameModePilot.Views;

/// <summary>设置视图（T17）：开机自启（默认关）+ 主题切换 + 版本信息。</summary>
public partial class SettingsView : UserControl
{
    /// <summary>初始化设置视图。</summary>
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (ThemeSystem.IsChecked == true)
        {
            App.ThemeService.Apply(PcOptThemeMode.System);
        }
        else if (ThemeLight.IsChecked == true)
        {
            App.ThemeService.Apply(PcOptThemeMode.Light);
        }
        else
        {
            App.ThemeService.Apply(PcOptThemeMode.Dark);
        }
    }
}
