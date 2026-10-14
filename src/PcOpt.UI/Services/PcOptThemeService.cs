// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PcOpt.UI.Services;

/// <summary>主题模式（用户选择）。</summary>
public enum ThemeMode
{
    /// <summary>跟随系统。</summary>
    System,

    /// <summary>浅色。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark,
}

/// <summary>生效主题（解析"跟随系统"后的结果）。</summary>
public enum ApplicationTheme
{
    /// <summary>浅色。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark,
}

/// <summary>
/// 主题服务（PRD 5.2 / 5.6 P0）：深浅色 + 跟随系统 + Mica，切换无闪烁。
/// 实现要点：
/// <list type="bullet">
/// <item>主题 = 整体替换 Application.Resources 中的 Palette 资源字典（PcOpt.* 键命名空间隔离，不冲突）；</item>
/// <item>控件一律 DynamicResource 引用 Token，字典替换即瞬时换色，无窗口隐藏/重显（无闪烁）；</item>
/// <item>跟随系统 = 1.5 秒轮询 HKCU ...\Personalize\AppsUseLightTheme（只读），系统切换主题后自动同步；</item>
/// <item>静态 <see cref="GlobalThemeChanged"/> 供组件库内部（如 FrameChart 重绘）订阅；
/// 实例 <see cref="ThemeChanged"/> 供应用层订阅。</item>
/// </list>
/// </summary>
public sealed class PcOptThemeService
{
    private const string LightPaletteUri = "/PcOpt.UI;component/Themes/Palette.Light.xaml";
    private const string DarkPaletteUri = "/PcOpt.UI;component/Themes/Palette.Dark.xaml";
    private const string SystemThemeRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemThemeRegistryValue = "AppsUseLightTheme";

    private readonly DispatcherTimer _systemWatcher;

    /// <summary>全局主题变更事件（组件库内部重绘用）。</summary>
    public static event EventHandler? GlobalThemeChanged;

    /// <summary>主题变更事件（应用层订阅）。</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>初始化主题服务（尚未应用任何主题）。</summary>
    public PcOptThemeService()
    {
        _systemWatcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _systemWatcher.Tick += OnSystemWatcherTick;
    }

    /// <summary>用户选择的模式。</summary>
    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>当前生效主题。</summary>
    public ApplicationTheme Effective { get; private set; } = ApplicationTheme.Dark;

    /// <summary>读取系统当前是否为浅色主题（只读注册表；读取失败按深色保守处理）。</summary>
    public static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SystemThemeRegistryKey);
            var value = key?.GetValue(SystemThemeRegistryValue);
            return value is int intValue && intValue != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            // 读不到系统偏好时保守返回深色（组件默认即深色，观感一致）
            return false;
        }
    }

    /// <summary>
    /// 应用指定主题模式（浅色 / 深色 / 跟随系统）。
    /// 在应用启动时或用户切换主题时调用；重复调用同模式为无操作。
    /// </summary>
    public void Apply(ThemeMode mode)
    {
        var effective = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => IsSystemLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark,
        };

        Mode = mode;
        var changed = Effective != effective || !_paletteApplied;
        Effective = effective;

        SwapPalette(effective);
        _paletteApplied = true;

        // 跟随系统时才需要轮询；固定模式下停止轮询省资源
        if (mode == ThemeMode.System)
        {
            _systemWatcher.Start();
        }
        else
        {
            _systemWatcher.Stop();
        }

        if (changed)
        {
            GlobalThemeChanged?.Invoke(this, EventArgs.Empty);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>为窗口应用 Mica 背景（Win11 22621+；不支持时由窗口自身背景兜底，PRD 5.1）。</summary>
    public static bool TryApplyMica(Window window) => WindowBackdropService.TryApplyMica(window);

    private bool _paletteApplied;

    private void OnSystemWatcherTick(object? sender, EventArgs e)
    {
        if (Mode != ThemeMode.System)
        {
            return;
        }

        var systemLight = IsSystemLightTheme();
        var effective = systemLight ? ApplicationTheme.Light : ApplicationTheme.Dark;
        if (effective == Effective)
        {
            return;
        }

        Effective = effective;
        SwapPalette(effective);
        GlobalThemeChanged?.Invoke(this, EventArgs.Empty);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 整体替换应用资源中的 Palette 字典（先移除旧再插入新，保证键唯一）。
    /// 插入位置 0：Palette 优先于应用级自定义字典（PcOpt.* 键专属，不影响他人键）。
    /// </summary>
    private static void SwapPalette(ApplicationTheme theme)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var targetUri = new Uri(theme == ApplicationTheme.Light ? LightPaletteUri : DarkPaletteUri, UriKind.Relative);

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source;
            if (source is not null
                && (string.Equals(source.OriginalString, LightPaletteUri, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(source.OriginalString, DarkPaletteUri, StringComparison.OrdinalIgnoreCase)))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = targetUri });
    }
}
