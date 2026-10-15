// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>Appx（预装应用）卸载风险等级（T28，PRD B-P0-8）。</summary>
public enum AppxRiskLevel
{
    /// <summary>系统 / 框架组件：不可卸载（零误删红线）。</summary>
    System,

    /// <summary>第三方或用途不明：谨慎处理。</summary>
    Caution,

    /// <summary>可安全卸载的预装应用（bloat）。</summary>
    Safe,
}

/// <summary>
/// Appx 卸载风险分级（T28，PRD B-P0-8："卸载列表风险标注 100%"）。
/// 纯函数：按包名前缀 / 子串匹配分类，保证任意包都能得到明确风险等级（覆盖率 100%）。
/// 系统 / 框架组件一律禁止卸载（零误删红线）。
/// </summary>
public static class AppxRiskGrader
{
    // 框架 / 运行时 / Shell / 系统服务：禁止卸载
    private static readonly string[] SystemMarkers =
    {
        "Microsoft.VCLibs",
        "Microsoft.NET.Native",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.UI.Xaml",
        "Microsoft.Services.Store.Engine",
        "Microsoft.WindowsStore",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.Windows.ShellExperienceHost",
        "Microsoft.Windows.StartMenuExperienceHost",
        "Microsoft.Windows.SecHealthUI",
        "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl",
        "Microsoft.Windows.Search",
        "Microsoft.Windows.ContentDeliveryManager",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.Windows.Cortana",
    };

    // 公认可安全卸载的预装应用（bloat）
    private static readonly string[] SafeMarkers =
    {
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.BingNews",
        "Microsoft.BingWeather",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.SkypeApp",
        "Microsoft.People",
        "Microsoft.WindowsMaps",
        "Microsoft.MixedReality.Portal",
        "Microsoft.Microsoft3DViewer",
        "Microsoft.Print3D",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.OneConnect",
        "Microsoft.Todos",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo",
        "Microsoft.MicrosoftStickyNotes",
        "Microsoft.XboxGameOverlay",
        "Microsoft.XboxGamingOverlay",
        "Microsoft.Xbox.TCUI",
    };

    /// <summary>判定某个 Appx 包的风险等级。</summary>
    /// <param name="packageName">包名（Name）。</param>
    /// <param name="publisher">发布者（PublisherDisplayName；可空）。</param>
    public static AppxRiskLevel Grade(string packageName, string? publisher)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return AppxRiskLevel.Caution;
        }

        if (SystemMarkers.Any(marker => packageName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return AppxRiskLevel.System;
        }

        if (SafeMarkers.Any(marker => packageName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return AppxRiskLevel.Safe;
        }

        return AppxRiskLevel.Caution;
    }

    /// <summary>风险等级中文文案。</summary>
    public static string RiskText(AppxRiskLevel risk) => risk switch
    {
        AppxRiskLevel.System => "系统组件",
        AppxRiskLevel.Caution => "谨慎",
        AppxRiskLevel.Safe => "可安全卸载",
        _ => "未知",
    };

    /// <summary>是否允许卸载（系统组件禁止）。</summary>
    public static bool CanRemove(AppxRiskLevel risk) => risk != AppxRiskLevel.System;

    /// <summary>人话建议。</summary>
    public static string Recommendation(AppxRiskLevel risk) => risk switch
    {
        AppxRiskLevel.System => "系统运行依赖它，不建议卸载",
        AppxRiskLevel.Caution => "用途不明或第三方应用，确认不再需要再卸载",
        AppxRiskLevel.Safe => "用不到的预装应用，卸载后可在应用商店重新安装",
        _ => string.Empty,
    };
}
