// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>一条已安装的 Appx 包（T28）。</summary>
/// <param name="Name">包名。</param>
/// <param name="PackageFullName">完整包标识（卸载用）。</param>
/// <param name="Version">版本。</param>
/// <param name="Publisher">发布者显示名。</param>
/// <param name="Risk">风险等级。</param>
public sealed record AppxPackageInfo(string Name, string PackageFullName, string Version, string Publisher, AppxRiskLevel Risk)
{
    /// <summary>风险文案。</summary>
    public string RiskText => AppxRiskGrader.RiskText(Risk);

    /// <summary>是否可卸载。</summary>
    public bool CanRemove => AppxRiskGrader.CanRemove(Risk);

    /// <summary>人话建议。</summary>
    public string Recommendation => AppxRiskGrader.Recommendation(Risk);
}

/// <summary>
/// Appx 预装应用管理服务（T28，PRD B-P0-8）。
/// 枚举 = <c>Get-AppxPackage</c>；卸载 = 按完整包名逐条 <c>Remove-AppxPackage</c>（绝不使用通配批量删除，零误删红线）；
/// 重装 = 从包缓存重新注册。残留 = 预览 <c>%LocalAppData%\Packages</c> 下同源目录（预览后才删除）。
/// </summary>
public sealed class AppxPackageService
{
    private readonly ILogger<AppxPackageService> _logger;

    /// <summary>初始化 Appx 管理服务。</summary>
    public AppxPackageService(ILogger<AppxPackageService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>枚举当前用户的 Appx 包（含风险分级）。</summary>
    public Result<IReadOnlyList<AppxPackageInfo>> Scan()
    {
        const string script =
            "Get-AppxPackage | Select-Object Name,PackageFullName,Version,PublisherDisplayName | " +
            "ConvertTo-Json -Compress -Depth 3";

        var run = PowerShellRunner.RunScript(script);
        if (!run.IsSuccess)
        {
            return Result<IReadOnlyList<AppxPackageInfo>>.Fail(run.Error);
        }

        return ParsePackages(run.Value ?? string.Empty);
    }

    /// <summary>卸载单个包（系统组件拒绝）。</summary>
    public Result<string> Remove(AppxPackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!package.CanRemove)
        {
            return Result<string>.Fail($"“{package.Name}”是系统组件，为安全起见不支持卸载");
        }

        var escaped = package.PackageFullName.Replace("'", "''");
        var run = PowerShellRunner.RunScript($"Remove-AppxPackage -Package '{escaped}' -ErrorAction Stop", 120_000);
        if (!run.IsSuccess)
        {
            return Result<string>.Fail($"卸载“{package.Name}”失败：{run.Error}");
        }

        _logger.LogInformation("已卸载 Appx 包（风险等级 {Risk}）", package.Risk);
        return Result<string>.Ok($"已卸载 {package.Name}，可在应用商店重新安装");
    }

    /// <summary>重新注册（重装）单个包。</summary>
    public Result<string> Reinstall(AppxPackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var escaped = package.Name.Replace("'", "''");
        var script =
            $"Get-AppxPackage -AllUsers -Name '{escaped}' | ForEach-Object {{ " +
            "Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppxManifest.xml') -ErrorAction SilentlyContinue }";
        var run = PowerShellRunner.RunScript(script, 120_000);
        return run.IsSuccess
            ? Result<string>.Ok($"已重新安装 {package.Name}")
            : Result<string>.Fail($"重新安装“{package.Name}”失败：{run.Error}");
    }

    /// <summary>
    /// 预览某包卸载后可能残留的本地数据目录（PRD B-P0-8："残留扫描可预览后删除"）。
    /// 仅返回路径供确认，不删除任何内容。
    /// </summary>
    public IReadOnlyList<string> PreviewResidual(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return Array.Empty<string>();
        }

        var packagesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");
        if (!Directory.Exists(packagesRoot))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateDirectories(packagesRoot)
                .Where(dir => Path.GetFileName(dir).Contains(packageName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private Result<IReadOnlyList<AppxPackageInfo>> ParsePackages(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<IReadOnlyList<AppxPackageInfo>>.Ok(Array.Empty<AppxPackageInfo>());
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            var packages = new List<AppxPackageInfo>(items.Count);
            foreach (var item in items)
            {
                var name = GetString(item, "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var publisher = GetString(item, "PublisherDisplayName");
                packages.Add(new AppxPackageInfo(
                    name,
                    GetString(item, "PackageFullName"),
                    GetString(item, "Version"),
                    publisher,
                    AppxRiskGrader.Grade(name, publisher)));
            }

            return Result<IReadOnlyList<AppxPackageInfo>>.Ok(
                packages.OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<AppxPackageInfo>>.Fail($"解析应用列表失败：{ex.Message}");
        }
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
