// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using PcOpt.UI.Services;
// .NET 9 起 Application/Window 自带 ThemeMode 实例属性会遮蔽同名枚举类型，用别名消歧
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.UI.Demo;

/// <summary>App 启动逻辑：初始化主题服务（跟随系统 + Mica）。</summary>
public partial class App : Application
{
    /// <summary>全局主题服务实例（Demo 与各产品启动方式一致）。</summary>
    internal static PcOptThemeService ThemeService { get; } = new();

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // PRD 5.2：深/浅色 + 跟随系统；Mica 背景由主窗口 Loaded 时应用（需 HWND）
        ThemeService.Apply(PcOptThemeMode.System);
    }
}
