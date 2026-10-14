// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;

namespace PcOpt.Products.GameModePilot;

/// <summary>
/// 产品 A（游戏模式自动调度器）应用入口。
/// 骨架阶段：主题初始资源由 App.xaml 的 ThemesDictionary 提供，深浅色跟随系统
/// 在 <see cref="MainWindow"/> 构造时经 SystemThemeWatcher.Watch 启用
/// （WPF-UI 4.x 的 Watch 需要 Window 实例，无法对 Application 调用）；
/// DI 容器与单实例在 T12 接入。
/// </summary>
public partial class App : Application
{
    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // T12：单实例互斥（命名 Mutex）与 DI 宿主初始化将在此展开。
    }
}
