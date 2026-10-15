// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Native;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>电源方案信息（T25，PRD B-P0-5）。</summary>
/// <param name="Guid">方案 GUID。</param>
/// <param name="Name">方案名。</param>
/// <param name="IsActive">是否为当前激活方案。</param>
public sealed record PowerSchemeInfo(Guid Guid, string Name, bool IsActive);

/// <summary>
/// 电源计划服务（T25，PRD B-P0-5）：一键导入 Ultimate Performance + 自定义高性能计划；
/// 列出 / 激活 / 删除方案。基于内核 <see cref="PowerCfgController"/>（powercfg 封装）。
/// </summary>
public sealed partial class PowerPlanService
{
    /// <summary>Windows 内置"终极性能（Ultimate Performance）"方案 GUID（经 duplicatescheme 派生）。</summary>
    public const string UltimatePerformanceTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    /// <summary>Windows 内置"高性能"方案 GUID。</summary>
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private readonly PowerCfgController _controller;
    private readonly ILogger<PowerPlanService> _logger;

    /// <summary>初始化电源计划服务。</summary>
    public PowerPlanService(PowerCfgController controller, ILogger<PowerPlanService> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>列出全部电源方案。</summary>
    public Result<IReadOnlyList<PowerSchemeInfo>> List()
    {
        var schemes = _controller.ListSchemes();
        if (!schemes.IsSuccess)
        {
            return Result<IReadOnlyList<PowerSchemeInfo>>.Fail(schemes.Error);
        }

        return Result<IReadOnlyList<PowerSchemeInfo>>.Ok(
            schemes.Value!.Select(scheme => new PowerSchemeInfo(scheme.Guid, scheme.Name, scheme.IsActive)).ToList());
    }

    /// <summary>激活指定电源方案。</summary>
    public Result<string> Activate(Guid guid)
    {
        var result = _controller.SetActiveScheme(guid);
        if (!result.IsSuccess)
        {
            return Result<string>.Fail(result.Error);
        }

        var list = List();
        var name = list.IsSuccess
            ? list.Value!.FirstOrDefault(scheme => scheme.Guid == guid)?.Name ?? guid.ToString()
            : guid.ToString();
        return Result<string>.Ok($"已切换到电源方案：{name}");
    }

    /// <summary>导入 Ultimate Performance 方案（通过 powercfg 复制内置模板），返回新方案 GUID。</summary>
    public Result<Guid> ImportUltimatePerformance()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("powercfg.exe")
            {
                Arguments = "-duplicatescheme " + UltimatePerformanceTemplateGuid,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return Result<Guid>.Fail("未能启动 powercfg");
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            var match = GuidRegex().Match(output);
            if (!match.Success || !Guid.TryParse(match.Value, out var guid))
            {
                return Result<Guid>.Fail("导入终极性能方案失败（该方案在部分系统上不受支持）");
            }

            _logger.LogInformation("已导入 Ultimate Performance 电源方案");
            return Result<Guid>.Ok(guid);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Result<Guid>.Fail($"导入终极性能方案失败：{ex.Message}");
        }
    }

    /// <summary>从 .pow 文件导入电源方案。</summary>
    public Result<Guid> ImportPow(string powFilePath)
    {
        if (string.IsNullOrWhiteSpace(powFilePath))
        {
            return Result<Guid>.Fail("电源方案文件路径为空");
        }

        return _controller.ImportScheme(powFilePath);
    }

    /// <summary>删除电源方案（可还原：内置方案可重建，用户方案需自行保留 .pow）。</summary>
    public Result Delete(Guid guid)
    {
        var result = _controller.DeleteScheme(guid);
        return result.IsSuccess ? Result.Ok() : Result.Fail(result.Error);
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRegex();
}
