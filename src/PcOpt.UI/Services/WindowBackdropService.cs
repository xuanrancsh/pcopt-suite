// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PcOpt.UI.Services;

/// <summary>
/// 窗口背景服务（PRD 5.1：Win11 Mica / 云母背景）。
/// 实现方式：DWM <c>DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)</c> 公开 API，
/// 不依赖第三方库（无需登记 NOTICE）。平台检查：Win11 22621+ 才支持 Mica，
/// 旧平台（Win10）返回 false，由窗口自身主题背景兜底（架构：Win10 22H2 兼容）。
/// </summary>
public static class WindowBackdropService
{
    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE（Win11 22621+ 公开属性）。</summary>
    private const int DwmwaSystemBackdropType = 38;

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE（Win11 22000+）。</summary>
    private const int DwmwaWindowCornerPreference = 33;

    /// <summary>DWMSBT_MAINWINDOW = Mica。</summary>
    private const int DwmsbtMainWindow = 2;

    /// <summary>DWMWCP_ROUND = 圆角窗口。</summary>
    private const int DwmcpRound = 2;

    /// <summary>Mica 最低支持 Build（Win11 22H2）。</summary>
    private const int MicaMinBuild = 22621;

    /// <summary>圆角窗口最低支持 Build（Win11 21H2）。</summary>
    private const int RoundedCornerMinBuild = 22000;

    /// <summary>当前系统是否支持 Mica（Win11 22621+）。</summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= MicaMinBuild;

    /// <summary>
    /// 尝试为窗口应用 Mica 背景；不支持或失败时返回 false（调用方用主题背景兜底）。
    /// </summary>
    public static bool TryApplyMica(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsMicaSupported)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var backdrop = DwmsbtMainWindow;
        var result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        return result == 0;
    }

    /// <summary>
    /// 尝试为窗口应用系统圆角（Win11 22000+）；旧平台无操作。
    /// </summary>
    public static bool TryApplyRoundedCorners(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Environment.OSVersion.Version.Build < RoundedCornerMinBuild)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var preference = DwmcpRound;
        var result = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        return result == 0;
    }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int attributeSize);
}
