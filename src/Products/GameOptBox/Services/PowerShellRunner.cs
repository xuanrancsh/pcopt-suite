// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Text;
using PcOpt.Core;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// PowerShell 脚本执行器（仅用于本地系统管理，如枚举 Appx 包 / 创建还原点）。
/// 通过 <c>-EncodedCommand</c> 传递脚本（UTF-16LE + Base64），从根上避免命令行引号转义漏洞。
/// 所有调用均为本地操作，不涉及网络（合规红线 #8）。
/// </summary>
internal static class PowerShellRunner
{
    /// <summary>执行脚本并返回标准输出；失败返回人话错误。</summary>
    /// <param name="script">PowerShell 脚本内容。</param>
    /// <param name="timeoutMs">超时（毫秒）。</param>
    public static Result<string> RunScript(string script, int timeoutMs = 60_000)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Result<string>.Fail("脚本内容为空");
        }

        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Result<string>.Fail("未能启动 PowerShell 进程");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // 进程已退出
                }

                return Result<string>.Fail("PowerShell 操作超时");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return Result<string>.Fail($"PowerShell 执行失败：{Truncate(error)}");
            }

            return Result<string>.Ok(output.Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Result<string>.Fail($"PowerShell 不可用：{ex.Message}");
        }
    }

    private static string Truncate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "（无错误详情）";
        }

        var single = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 200 ? single : single[..200];
    }
}
