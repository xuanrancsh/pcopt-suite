// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PcOpt.Core;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 开机自启服务（T17，PRD A-P0-9）。
/// 实现：HKCU <c>Software\Microsoft\Windows\CurrentVersion\Run</c>（当前用户，无需管理员）。
/// 合规说明：写入的是"应用自身注册项"（A 应用的自启动），非对操作系统的 tweak，
/// 因而不经内核 tweak handler；仅 HKCU（用户级），可随时由用户关闭并在设置中撤回。
/// 默认<b>不</b>开启，需用户在设置 / 向导中显式勾选（PRD：开机自启可选）。
/// </summary>
public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameModePilot";

    private readonly ILogger<AutoStartService> _logger;

    /// <summary>初始化服务。</summary>
    public AutoStartService(ILogger<AutoStartService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>当前是否已设置开机自启。</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>设置 / 取消开机自启。</summary>
    public Result SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                return Result.Fail("无法打开开机自启注册表项");
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    return Result.Fail("无法确定程序自身路径，未能设置开机自启");
                }

                key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
                _logger.LogInformation("已设置开机自启");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("已取消开机自启");
            }

            return Result.Ok();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"设置开机自启失败：{ex.Message}");
        }
    }
}
