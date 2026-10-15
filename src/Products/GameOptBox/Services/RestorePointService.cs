// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 系统还原点创建服务抽象（T29，PRD B-P0-9："一键创建系统还原点"）。
/// 抽象出来便于单元测试替换为替身（真实实现调用 PowerShell 的 Checkpoint-Computer）。
/// </summary>
public interface IRestorePointService
{
    /// <summary>创建系统还原点。</summary>
    /// <param name="description">还原点描述（不含用户数据）。</param>
    PcOpt.Core.Result<string> Create(string description);
}

/// <summary>
/// Windows 系统还原点创建实现：<c>Enable-ComputerRestore</c> + <c>Checkpoint-Computer</c>。
/// 需管理员权限；系统还原功能被禁用或权限不足时返回人话错误（不抛异常）。
/// </summary>
public sealed class WindowsRestorePointService : IRestorePointService
{
    /// <inheritdoc/>
    public PcOpt.Core.Result<string> Create(string description)
    {
        // 描述经消毒：仅保留中文、字母、数字、空格与常见标点，杜绝命令注入
        var safe = Sanitize(description);
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "pcopt 优化前还原点";
        }

        var command =
            "Enable-ComputerRestore -Drive $env:SystemDrive -ErrorAction SilentlyContinue; " +
            $"Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS";

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return PcOpt.Core.Result<string>.Fail("未能启动系统还原点创建进程");
            }

            if (!process.WaitForExit(120_000))
            {
                return PcOpt.Core.Result<string>.Fail("创建系统还原点超时，请稍后重试");
            }

            return process.ExitCode == 0
                ? PcOpt.Core.Result<string>.Ok($"已创建系统还原点：{safe}")
                : PcOpt.Core.Result<string>.Fail("创建系统还原点失败（可能未开启系统还原或权限不足）");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return PcOpt.Core.Result<string>.Fail($"创建系统还原点失败：{ex.Message}");
        }
    }

    private static string Sanitize(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(description.Length);
        foreach (var ch in description)
        {
            // 允许中文、字母、数字、空格、连字符、下划线、圆点
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }
}
