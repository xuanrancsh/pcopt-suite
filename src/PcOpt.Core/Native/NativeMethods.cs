// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PcOpt.Core.Native;

/// <summary>
/// Win32 / ntdll P/Invoke 声明集合（内核唯一原生入口，审计面集中，架构 2 依赖规则 4）。
/// 合规声明：本文件（及整个 PcOpt.Core）不包含任何 DLL 注入 / 远程线程 / 内存读写 API
/// （PRD 红线 #2 / #4），可公开审计。
/// </summary>
internal static class NativeMethods
{
    // ---------- kernel32：文件原子替换（MoveFileEx REPLACE_EXISTING） ----------

    /// <summary>MOVEFILE_REPLACE_EXISTING。</summary>
    internal const uint MoveFileReplaceExisting = 0x00000001;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    // ---------- advapi32：SCM 服务控制（架构 3.6） ----------

    /// <summary>SC_MANAGER_CONNECT。</summary>
    internal const uint ScManagerConnect = 0x00000001;

    /// <summary>SERVICE_QUERY_CONFIG。</summary>
    internal const uint ServiceQueryConfig = 0x00000001;

    /// <summary>SERVICE_CHANGE_CONFIG。</summary>
    internal const uint ServiceChangeConfig = 0x00000002;

    /// <summary>SERVICE_NO_CHANGE（保留原值）。</summary>
    internal const uint ServiceNoChange = 0xFFFFFFFF;

    /// <summary>QUERY_SERVICE_CONFIG 结构中 dwStartType 字段的字节偏移（dwServiceType 之后）。</summary>
    internal const int QueryServiceConfigStartTypeOffset = 4;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceConfigW(IntPtr hService, IntPtr lpServiceConfig, uint cbBufSize, out uint pcbBytesNeeded);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfigW(
        IntPtr hService,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string? lpBinaryPathName,
        string? lpLoadOrderGroup,
        IntPtr lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword,
        string? lpDisplayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr hSCObject);

    // ---------- kernel32：进程优先级 / 亲和性 / EcoQoS（架构 3.6） ----------

    /// <summary>PROCESS_SET_INFORMATION。</summary>
    internal const uint ProcessSetInformation = 0x00000200;

    /// <summary>PROCESS_QUERY_LIMITED_INFORMATION。</summary>
    internal const uint ProcessQueryLimitedInformation = 0x00001000;

    /// <summary>ProcessPowerThrottling 信息类。</summary>
    internal const int ProcessPowerThrottling = 4;

    /// <summary>PROCESS_POWER_THROTTLING_EXECUTION_SPEED（EcoQoS 控制位）。</summary>
    internal const uint PowerThrottlingExecutionSpeed = 0x00000001;

    /// <summary>PROCESS_POWER_THROTTLING_INFORMATION（SetProcessInformation / GetProcessInformation）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PowerThrottlingState
    {
        /// <summary>结构版本（POWER_THROTTLING_STATE_VERSION = 1，必须置于首位）。</summary>
        public uint Version;

        /// <summary>控制掩码（哪些字段生效）。</summary>
        public uint ControlMask;

        /// <summary>状态掩码（启用 / 禁用）。</summary>
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref PowerThrottlingState processInformation, uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessInformation(IntPtr hProcess, int processInformationClass, ref PowerThrottlingState processInformation, uint processInformationSize);

    // ---------- ntdll：计时器分辨率（架构 3.6） ----------

    /// <summary>
    /// 请求 / 释放计时器分辨率。SetResolution=true 为请求指定分辨率；false 且 DesiredResolution=0 为释放请求。
    /// </summary>
    [DllImport("ntdll.dll", SetLastError = true)]
    internal static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint currentResolution);

    /// <summary>查询系统计时器分辨率支持范围与当前值（100ns 单位）。</summary>
    [DllImport("ntdll.dll")]
    internal static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    /// <summary>将 Win32 错误码格式化为可读消息（不含任何路径信息）。</summary>
    internal static string FormatWin32Error(int errorCode) => new Win32Exception(errorCode).Message;

    // ---------- kernel32：逻辑处理器拓扑（大小核，架构 3.3） ----------

    /// <summary>LOGICAL_PROCESSOR_RELATIONSHIP：RelationProcessorCore。</summary>
    internal const int RelationProcessorCore = 0;

    /// <summary>PROCESSOR_RELATION 结构中 EfficiencyClass 字段相对结构起始的偏移。</summary>
    internal const int ProcessorRelationEfficiencyClassOffset = 0;

    /// <summary>PROCESSOR_RELATION 结构中 GroupCount 字段相对 ProcessorRelation 起始的偏移。</summary>
    internal const int ProcessorRelationGroupCountOffset = 20;

    /// <summary>GroupMask 数组相对 ProcessorRelation 起始的偏移（GROUP_AFFINITY 按 8 字节对齐）。</summary>
    internal const int ProcessorRelationGroupMaskOffset = 32;

    /// <summary>GROUP_AFFINITY 结构大小（KAFFINITY 8 字节 + GROUP 2 字节 + 对齐填充）。</summary>
    internal const int GroupAffinitySize = 16;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);
}
