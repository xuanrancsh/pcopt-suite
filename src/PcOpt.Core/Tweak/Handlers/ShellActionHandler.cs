// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// Shell 命令处理器（架构 3.1 / T5，Process.Start 封装）。
/// 注意：Shell 动作本质不可自动还原——Revert 返回明确失败（规则作者应提供配对还原命令，
/// 或引导用户手动处理）。命令来源必须是已签名官方规则包或用户确认过的本地规则
/// （加载链路见 <see cref="TweakEngine.LoadRulePackage"/>）。
/// .bat / .cmd 自动经 <c>cmd.exe /c</c> 执行；日志绝不记录命令行参数（可能含用户路径，架构 8.3）。
/// </summary>
public sealed class ShellActionHandler : ITweakActionHandler
{
    /// <summary>命令执行超时（秒）。</summary>
    public const int ExecutionTimeoutSeconds = 60;

    private readonly ILogger<ShellActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public ShellActionHandler(ILogger<ShellActionHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is ShellAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        _ = RequireAction(action);
        // Shell 动作没有可快照的系统原值；记录事实快照供审计
        const string snapshot = "{\"shell\":true}";
        return Result<string>.Ok(snapshot);
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        if (string.IsNullOrWhiteSpace(typed.Command))
        {
            return Result.Fail("Shell 动作缺少命令");
        }

        var (fileName, arguments) = ResolveInvocation(typed.Command, typed.Arguments ?? string.Empty);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Result.Fail("无法启动命令进程");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(ExecutionTimeoutSeconds * 1000))
            {
                process.Kill(entireProcessTree: true);
                return Result.Fail($"命令执行超时（{ExecutionTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} 秒）");
            }

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return Result.Fail(
                    $"命令退出码 {process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}：{(detail.Length > 200 ? detail[..200] : detail).Trim()}");
            }

            _logger.LogDebug("Shell 命令已执行（退出码 0）");
            return Result.Ok();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return Result.Fail($"无法执行命令“{typed.Command}”：{ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail($"无法执行命令“{typed.Command}”：{ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        _ = RequireAction(action);
        return Result.Fail("Shell 命令无法自动还原（请使用规则作者提供的配对还原命令，或手动处理）");
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        _ = RequireAction(action);
        // Shell 动作无读回语义；正确性已由 Apply 的退出码校验保证
        return Result.Ok();
    }

    /// <summary>.bat / .cmd 经 cmd.exe /c 执行；其余命令直接启动。</summary>
    internal static (string FileName, string Arguments) ResolveInvocation(string command, string arguments)
    {
        var extension = Path.GetExtension(command.Trim()).ToLowerInvariant();
        if (extension is ".bat" or ".cmd")
        {
            var combined = string.IsNullOrWhiteSpace(arguments)
                ? $"\"{command.Trim()}\""
                : $"\"{command.Trim()}\" {arguments}";
            return ("cmd.exe", $"/c {combined}");
        }

        return (command.Trim(), arguments ?? string.Empty);
    }

    private static ShellAction RequireAction(TweakAction action) =>
        action as ShellAction
        ?? throw new ArgumentException($"本处理器只接受 ShellAction，收到 {action.GetType().Name}", nameof(action));
}
