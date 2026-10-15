// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 系统服务目录服务（T22，PRD B-P0-1）。
/// 枚举：只读注册表 <c>HKLM\SYSTEM\CurrentControlSet\Services</c>（含 Type 过滤为"用户态服务"，典型 200+ 项），
/// 逐项结合 <see cref="ServiceKnowledgeBase"/> 给出人话说明与推荐启动类型。
/// 只读枚举不改动系统；改动一律经 <see cref="ServiceOptimizationPlanner"/> 生成规则由引擎 apply/revert。
/// </summary>
public sealed class ServiceCatalogService
{
    private const string ServicesKeyPath = @"SYSTEM\CurrentControlSet\Services";
    private const int TypeWin32OwnProcess = 0x10;
    private const int TypeWin32ShareProcess = 0x20;

    private readonly ILogger<ServiceCatalogService> _logger;

    /// <summary>初始化服务目录服务。</summary>
    /// <param name="serviceControl">内核 SCM 封装（保留依赖，便于后续读取实时状态）。</param>
    /// <param name="logger">日志。</param>
    public ServiceCatalogService(PcOpt.Core.Native.ServiceControl serviceControl, ILogger<ServiceCatalogService> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceControl);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>枚举系统用户态服务（只读）。</summary>
    public Result<IReadOnlyList<ServiceEntry>> Enumerate()
    {
        try
        {
            using var servicesKey = Registry.LocalMachine.OpenSubKey(ServicesKeyPath, writable: false);
            if (servicesKey is null)
            {
                return Result<IReadOnlyList<ServiceEntry>>.Fail("无法读取系统服务注册表（权限不足）");
            }

            var entries = new List<ServiceEntry>();
            foreach (var name in servicesKey.GetSubKeyNames())
            {
                using var serviceKey = servicesKey.OpenSubKey(name, writable: false);
                if (serviceKey is null)
                {
                    continue;
                }

                var type = serviceKey.GetValue("Type") is int typeValue ? typeValue : 0;
                // 仅保留用户态服务（Win32 独占 / 共享进程），排除驱动类条目
                if ((type & (TypeWin32OwnProcess | TypeWin32ShareProcess)) == 0)
                {
                    continue;
                }

                var start = MapStart(serviceKey.GetValue("Start"));
                var displayName = serviceKey.GetValue("DisplayName") as string;
                var knowledge = ServiceKnowledgeBase.Describe(name, displayName);

                entries.Add(new ServiceEntry(
                    name,
                    string.IsNullOrWhiteSpace(displayName) ? name : displayName!,
                    start,
                    knowledge.Recommended,
                    knowledge.NonEssential,
                    knowledge.Description));
            }

            _logger.LogInformation("枚举到 {Count} 个用户态服务", entries.Count);
            return Result<IReadOnlyList<ServiceEntry>>.Ok(
                entries.OrderBy(entry => entry.DisplayName, StringComparer.CurrentCulture).ToList());
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return Result<IReadOnlyList<ServiceEntry>>.Fail($"枚举系统服务失败：{ex.Message}");
        }
    }

    private static ServiceStartMode MapStart(object? startValue)
    {
        var value = startValue is int start ? start : -1;
        return value switch
        {
            0 => ServiceStartMode.Boot,
            1 => ServiceStartMode.System,
            2 => ServiceStartMode.Automatic,
            3 => ServiceStartMode.Manual,
            4 => ServiceStartMode.Disabled,
            _ => ServiceStartMode.Automatic,
        };
    }
}
