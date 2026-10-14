// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PcOpt.Products.FrameLab;

/// <summary>
/// 产品 C 主窗口（骨架）。T31 落地导航（测试台/报告/诊断/历史/设置）+ 实时迷你图。
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
