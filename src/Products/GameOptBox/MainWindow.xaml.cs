// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PcOpt.Products.GameOptBox;

/// <summary>
/// 产品 B 主窗口（骨架）。T20 落地左侧导航（8 模块）+ 首页健康分。
/// </summary>
public partial class MainWindow
{
    /// <summary>初始化主窗口。</summary>
    public MainWindow()
    {
        InitializeComponent();

        // 跟随系统深浅色：Watch 首次调用即按当前系统主题应用一次，
        // 之后监听 WM_WININICHANGE 自动同步主题 / 强调色 / Mica 背景
        // （WPF-UI 官方推荐用法；不支持 Mica 的系统自动降级，无副作用）。
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
    }
}
