// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// 进程优先级 / 亲和性处理器（架构 3.1 / T5）。
/// EcoQoS 语义：当目标优先级为 <see cref="ProcessPriorityClass.Idle"/>（后台进程降级）时，
/// 同时启用 EcoQoS（PROCESS_POWER_THROTTLING_EXECUTION_SPEED），实现"省电降温"的后台抑制；
/// 其他优先级不启用。原 EcoQoS 状态记录进快照，还原时写回。
/// 进程名匹配第一个实例（游戏通常单实例；多实例场景由 T13 SessionEngine 扩展）。
/// 合规声明：仅使用用户态进程调度 API，不读取 / 不写入目标进程内存（PRD 红线 #4）。
/// </summary>
public sealed class ProcessPriorityActionHandler : ITweakActionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ProcessPriorityController _controller;
    private readonly ILogger<ProcessPriorityActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public ProcessPriorityActionHandler(ProcessPriorityController controller, ILogger<ProcessPriorityActionHandler> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is ProcessPriorityAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        var typed = RequireAction(action);
        var processId = FindProcessId(typed.ProcessName);
        if (processId is null)
        {
            // 进程未运行：记录"不存在"，Apply 时明确失败（不猜测、不等待）
            var absent = new ProcessPrioritySnapshot(false, null, null, null);
            return Result<string>.Ok(JsonSerializer.Serialize(absent, JsonOptions));
        }

        var query = _controller.Query(processId.Value);
        if (!query.IsSuccess)
        {
            return Result<string>.Fail(query.Error);
        }

        // EcoQoS 查询不可用时（Windows 10，见 ProcessPriorityController.Query 注释）按
        // Windows 默认状态"禁用"记录：本处理器只会启用 EcoQoS、从不禁用，
        // 还原时写回 false 即是自身操作的精确逆操作，保证"还原优先"不落空。
        var snapshot = query.Value! with { EcoQosEnabled = query.Value!.EcoQosEnabled ?? false };
        return Result<string>.Ok(JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        var processId = FindProcessId(typed.ProcessName);
        if (processId is null)
        {
            return Result.Fail($"进程“{typed.ProcessName}”未运行，无法应用优先级调整");
        }

        var query = _controller.Query(processId.Value);
        if (query.IsSuccess && query.Value is { } snapshot)
        {
            // 幂等：所有目标项均已一致则跳过
            var ecoTarget = typed.Priority == ProcessPriorityClass.Idle;
            if (snapshot.Priority == typed.Priority
                && (typed.AffinityMask is null || snapshot.AffinityMask == typed.AffinityMask)
                && (!ecoTarget || snapshot.EcoQosEnabled == true))
            {
                return Result.Ok();
            }
        }

        var priorityResult = _controller.SetPriority(processId.Value, typed.Priority);
        if (!priorityResult.IsSuccess)
        {
            return priorityResult;
        }

        if (typed.AffinityMask is { } affinityMask)
        {
            var affinityResult = _controller.SetAffinity(processId.Value, affinityMask);
            if (!affinityResult.IsSuccess)
            {
                return affinityResult;
            }
        }

        if (typed.Priority == ProcessPriorityClass.Idle)
        {
            var ecoResult = _controller.SetEcoQos(processId.Value, enable: true);
            if (!ecoResult.IsSuccess)
            {
                return ecoResult;
            }
        }

        _logger.LogDebug("进程调度已调整（进程名: {ProcessName}）", typed.ProcessName);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        var typed = RequireAction(action);
        ProcessPrioritySnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ProcessPrioritySnapshot>(originalValueJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("还原日志中的进程调度快照损坏");
        }

        if (snapshot is null || !snapshot.ProcessExists)
        {
            // 原本进程未运行，无可还原
            return Result.Ok();
        }

        var processId = FindProcessId(typed.ProcessName);
        if (processId is null)
        {
            // 进程已退出，调度状态随进程消失，无需还原
            return Result.Ok();
        }

        if (snapshot.Priority is { } originalPriority)
        {
            var result = _controller.SetPriority(processId.Value, originalPriority);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        if (snapshot.AffinityMask is { } originalAffinity)
        {
            var result = _controller.SetAffinity(processId.Value, originalAffinity);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        if (snapshot.EcoQosEnabled is { } originalEcoQos)
        {
            var result = _controller.SetEcoQos(processId.Value, originalEcoQos);
            if (!result.IsSuccess)
            {
                return result;
            }
        }

        _logger.LogDebug("进程调度已还原（进程名: {ProcessName}）", typed.ProcessName);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        var typed = RequireAction(action);
        var processId = FindProcessId(typed.ProcessName);
        if (processId is null)
        {
            return Result.Fail($"校验失败：进程“{typed.ProcessName}”未运行");
        }

        var query = _controller.Query(processId.Value);
        if (!query.IsSuccess)
        {
            return Result.Fail(query.Error);
        }

        if (query.Value is not { } snapshot)
        {
            return Result.Fail($"校验失败：无法读取进程“{typed.ProcessName}”的调度状态");
        }

        if (snapshot.Priority != typed.Priority)
        {
            return Result.Fail($"校验失败：进程“{typed.ProcessName}”优先级为 {snapshot.Priority}，期望 {typed.Priority}");
        }

        if (typed.AffinityMask is { } expectedAffinity && snapshot.AffinityMask != expectedAffinity)
        {
            return Result.Fail($"校验失败：进程“{typed.ProcessName}”亲和性与目标不一致");
        }

        if (typed.Priority == ProcessPriorityClass.Idle && snapshot.EcoQosEnabled == false)
        {
            // 仅当 EcoQoS 状态"可查询且确认未生效"时判失败；
            // 查询不可用（Windows 10）时为 null，此时 SetEcoQos 调用成功即视为已生效
            // （SetProcessInformation 在该类上各系统均受支持）。
            return Result.Fail($"校验失败：进程“{typed.ProcessName}”EcoQoS 未生效");
        }

        return Result.Ok();
    }

    /// <summary>按进程名（可带 .exe）查找第一个实例的进程 Id。</summary>
    internal static int? FindProcessId(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var name = processName.Trim();
        var fileName = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = name;
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(fileName);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return processes.Length > 0 ? processes[0].Id : null;
    }

    private static ProcessPriorityAction RequireAction(TweakAction action) =>
        action as ProcessPriorityAction
        ?? throw new ArgumentException($"本处理器只接受 ProcessPriorityAction，收到 {action.GetType().Name}", nameof(action));
}
