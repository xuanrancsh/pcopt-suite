// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PcOpt.Core.Native;

/// <summary>电源方案信息。</summary>
/// <param name="Guid">方案 GUID。</param>
/// <param name="Name">方案名称。</param>
/// <param name="IsActive">是否为当前活动方案。</param>
public sealed record PowerScheme(Guid Guid, string Name, bool IsActive);

/// <summary>
/// powercfg.exe 封装（架构 3.6：电源计划 list / import / setactive / delete）。
/// 以子进程方式调用；输出解析仅取 GUID，不记录任何用户路径（架构 8.3）。
/// </summary>
public sealed class PowerCfgController
{
    /// <summary>powercfg 子进程名称。</summary>
    public const string ExecutableName = "powercfg";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>powercfg 输出中 GUID 的匹配模式（如 "Power Scheme GUID: 381b4222-..."）。</summary>
    private static readonly Regex GuidPattern = new(
        @"GUID:\s*([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>列出全部电源方案。</summary>
    public Result<IReadOnlyList<PowerScheme>> ListSchemes()
    {
        var run = Run("/list");
        if (!run.IsSuccess)
        {
            return Result<IReadOnlyList<PowerScheme>>.Fail(run.Error);
        }

        var schemes = new List<PowerScheme>();
        foreach (var line in run.Value!.Split('\n'))
        {
            var match = GuidPattern.Match(line);
            if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var guid))
            {
                continue;
            }

            var name = ExtractSchemeName(line);
            schemes.Add(new PowerScheme(guid, name, line.Contains('*')));
        }

        return Result<IReadOnlyList<PowerScheme>>.Ok(schemes);
    }

    /// <summary>获取当前活动电源方案的 GUID。</summary>
    public Result<Guid> GetActiveSchemeGuid()
    {
        var run = Run("/getactivescheme");
        if (!run.IsSuccess)
        {
            return Result<Guid>.Fail(run.Error);
        }

        var match = GuidPattern.Match(run.Value!);
        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var guid))
        {
            return Result<Guid>.Fail("无法从 powercfg 输出解析当前电源方案 GUID");
        }

        return Result<Guid>.Ok(guid);
    }

    /// <summary>导入 .pow 电源方案文件，返回新方案 GUID。</summary>
    public Result<Guid> ImportScheme(string powFilePath)
    {
        if (string.IsNullOrWhiteSpace(powFilePath))
        {
            return Result<Guid>.Fail("电源方案文件路径不能为空");
        }

        var run = Run($"/import \"{powFilePath}\"");
        if (!run.IsSuccess)
        {
            return Result<Guid>.Fail($"导入电源方案失败：{run.Error}");
        }

        var match = GuidPattern.Match(run.Value!);
        if (!match.Success || !Guid.TryParse(match.Groups[1].Value, out var guid))
        {
            return Result<Guid>.Fail("无法从 powercfg 输出解析导入的电源方案 GUID");
        }

        return Result<Guid>.Ok(guid);
    }

    /// <summary>激活指定电源方案。</summary>
    public Result SetActiveScheme(Guid schemeGuid)
    {
        var run = Run($"/setactive {schemeGuid:D}");
        return run.IsSuccess
            ? Result.Ok()
            : Result.Fail($"激活电源方案失败：{run.Error}");
    }

    /// <summary>删除指定电源方案。</summary>
    public Result DeleteScheme(Guid schemeGuid)
    {
        var run = Run($"/delete {schemeGuid:D}");
        return run.IsSuccess
            ? Result.Ok()
            : Result.Fail($"删除电源方案失败：{run.Error}");
    }

    private static string ExtractSchemeName(string line)
    {
        var start = line.IndexOf('(');
        var end = line.LastIndexOf(')');
        return start >= 0 && end > start
            ? line[(start + 1)..end].Trim()
            : string.Empty;
    }

    private static Result<string> Run(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ExecutableName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // powercfg 在中文 Windows 按 OEM 代码页（936/GBK）输出中文方案名；
                // 固定 UTF-8 会把中文解成乱码（GUID 却正常，极具迷惑性）。
                StandardOutputEncoding = ConsoleOutputDecoder.OutputEncoding,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Result<string>.Fail("无法启动 powercfg 进程");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return Result<string>.Fail("powercfg 执行超时");
            }

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return Result<string>.Fail(
                    $"powercfg 返回退出码 {process.ExitCode.ToString(CultureInfo.InvariantCulture)}：{detail.Trim()}");
            }

            return Result<string>.Ok(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Result<string>.Fail($"无法执行 powercfg：{ex.Message}");
        }
    }
}
