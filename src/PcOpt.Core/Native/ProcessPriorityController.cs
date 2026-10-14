// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Runtime.InteropServices;
using PcOpt.Core.Tweak;
// 别名消歧：内核自有优先级枚举（Win32 编码）与 System.Diagnostics.ProcessPriorityClass 同名
using ProcessPriorityClass = PcOpt.Core.Tweak.ProcessPriorityClass;

namespace PcOpt.Core.Native;

/// <summary>
/// 进程优先级快照（不可变记录）。
/// </summary>
/// <param name="ProcessExists">进程是否存在（OpenProcess 是否成功）。</param>
/// <param name="Priority">当前优先级类；无法识别时为 null。</param>
/// <param name="AffinityMask">当前亲和性掩码；无法获取时为 null。</param>
/// <param name="EcoQosEnabled">当前 EcoQoS（执行速度限制）状态；无法获取时为 null。</param>
public sealed record ProcessPrioritySnapshot(
    bool ProcessExists,
    ProcessPriorityClass? Priority,
    long? AffinityMask,
    bool? EcoQosEnabled);

/// <summary>
/// 进程优先级 / 亲和性 / EcoQoS 控制器（架构 3.6：SetPriorityClass / SetProcessAffinityMask / EcoQoS）。
/// 合规声明：仅使用用户态进程调度 API，不读取 / 不写入目标进程内存（PRD 红线 #4）。
/// </summary>
public sealed class ProcessPriorityController
{
    /// <summary>查询进程当前调度状态。</summary>
    public Result<ProcessPrioritySnapshot> Query(int processId)
    {
        if (processId <= 0)
        {
            return Result<ProcessPrioritySnapshot>.Fail("进程 Id 无效");
        }

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            bInheritHandle: false,
            processId);
        if (handle == IntPtr.Zero)
        {
            // 打不开句柄：进程已退出或无权访问（按"不存在"处理，由调用方决定提示）
            return Result<ProcessPrioritySnapshot>.Ok(new ProcessPrioritySnapshot(false, null, null, null));
        }

        try
        {
            var rawPriority = NativeMethods.GetPriorityClass(handle);
            ProcessPriorityClass? priority = rawPriority != 0 && Enum.IsDefined((ProcessPriorityClass)rawPriority)
                ? (ProcessPriorityClass)rawPriority
                : null;

            long? affinityMask = null;
            if (NativeMethods.GetProcessAffinityMask(handle, out var processAffinity, out _) && processAffinity != IntPtr.Zero)
            {
                affinityMask = processAffinity.ToInt64();
            }

            bool? ecoQos = null;
            var throttling = new NativeMethods.PowerThrottlingState { Version = 1 };
            if (NativeMethods.GetProcessInformation(
                    handle,
                    NativeMethods.ProcessPowerThrottling,
                    ref throttling,
                    (uint)Marshal.SizeOf<NativeMethods.PowerThrottlingState>()))
            {
                var controlled = (throttling.ControlMask & NativeMethods.PowerThrottlingExecutionSpeed) != 0;
                ecoQos = controlled && (throttling.StateMask & NativeMethods.PowerThrottlingExecutionSpeed) != 0;
            }
            else
            {
                // Windows 10 例外：GetProcessInformation 不接纳 ProcessPowerThrottling 信息类
                // （微软 Q&A 确认该类仅 SetProcessInformation 支持，查询恒返回
                // ERROR_INVALID_PARAMETER）。此时 EcoQoS 状态记为"未知"（null），
                // 由调用方按"无法确认"降级处理，不作为错误上抛。
                ecoQos = null;
            }

            return Result<ProcessPrioritySnapshot>.Ok(new ProcessPrioritySnapshot(true, priority, affinityMask, ecoQos));
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>设置进程优先级类。</summary>
    public Result SetPriority(int processId, ProcessPriorityClass priority)
    {
        var handle = OpenForSetInformation(processId);
        if (handle == IntPtr.Zero)
        {
            return Result.Fail(DescribeOpenFailure(processId));
        }

        try
        {
            if (!NativeMethods.SetPriorityClass(handle, (uint)priority))
            {
                return Result.Fail($"无法设置进程优先级：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            return Result.Ok();
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>设置进程亲和性掩码（必须是系统亲和性的非空子集）。</summary>
    public Result SetAffinity(int processId, long affinityMask)
    {
        if (affinityMask <= 0)
        {
            return Result.Fail("亲和性掩码必须为正数");
        }

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation | NativeMethods.ProcessSetInformation,
            bInheritHandle: false,
            processId);
        if (handle == IntPtr.Zero)
        {
            return Result.Fail(DescribeOpenFailure(processId));
        }

        try
        {
            if (!NativeMethods.GetProcessAffinityMask(handle, out _, out var systemAffinity))
            {
                return Result.Fail($"无法获取系统亲和性掩码：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            if ((affinityMask & ~systemAffinity.ToInt64()) != 0)
            {
                return Result.Fail("亲和性掩码超出系统逻辑处理器范围");
            }

            if (!NativeMethods.SetProcessAffinityMask(handle, new IntPtr(affinityMask)))
            {
                return Result.Fail($"无法设置进程亲和性：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            return Result.Ok();
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>启用 / 禁用 EcoQoS（PROCESS_POWER_THROTTLING_EXECUTION_SPEED）。</summary>
    public Result SetEcoQos(int processId, bool enable)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation | NativeMethods.ProcessSetInformation,
            bInheritHandle: false,
            processId);
        if (handle == IntPtr.Zero)
        {
            return Result.Fail(DescribeOpenFailure(processId));
        }

        try
        {
            var state = new NativeMethods.PowerThrottlingState
            {
                // Version 必须为 1（POWER_THROTTLING_STATE 结构体版本），否则 SetProcessInformation
                // 以 ERROR_BAD_LENGTH 拒绝——结构体实际占用 12 字节（Version + ControlMask + StateMask）。
                Version = 1,
                ControlMask = NativeMethods.PowerThrottlingExecutionSpeed,
                StateMask = enable ? NativeMethods.PowerThrottlingExecutionSpeed : 0,
            };
            if (!NativeMethods.SetProcessInformation(
                    handle,
                    NativeMethods.ProcessPowerThrottling,
                    ref state,
                    (uint)Marshal.SizeOf<NativeMethods.PowerThrottlingState>()))
            {
                return Result.Fail($"无法设置 EcoQoS：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            return Result.Ok();
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    private static IntPtr OpenForSetInformation(int processId) =>
        NativeMethods.OpenProcess(NativeMethods.ProcessSetInformation, bInheritHandle: false, processId);

    private static string DescribeOpenFailure(int processId) =>
        $"无法打开进程（进程 Id: {processId.ToString(System.Globalization.CultureInfo.InvariantCulture)}）：可能已退出或无权访问";
}
