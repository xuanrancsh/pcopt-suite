// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Win32;

namespace PcOpt.Core.Tweak;

/// <summary>
/// 服务启动类型（SCM dwStartType 语义，架构 3.1）。
/// </summary>
public enum ServiceStartMode
{
    /// <summary>由启动加载器加载（驱动）。</summary>
    Boot = 0,

    /// <summary>由 IoInitSystem 加载。</summary>
    System = 1,

    /// <summary>自动启动。</summary>
    Automatic = 2,

    /// <summary>手动启动。</summary>
    Manual = 3,

    /// <summary>已禁用。</summary>
    Disabled = 4,
}

/// <summary>
/// 电源计划操作类型（powercfg 封装，架构 3.6）。
/// </summary>
public enum PowerActionKind
{
    /// <summary>列出全部电源方案（只读查询）。</summary>
    List,

    /// <summary>导入 .pow 电源方案文件。</summary>
    Import,

    /// <summary>激活指定 GUID 的电源方案。</summary>
    SetActive,

    /// <summary>删除指定 GUID 的电源方案。</summary>
    Delete,
}

/// <summary>
/// 进程优先级类（Win32 优先级编码；YAML 中写枚举名，如 <c>High</c>）。
/// </summary>
public enum ProcessPriorityClass
{
    /// <summary>IDLE_PRIORITY_CLASS（0x40）。后台进程降级到此级时 handler 会同时启用 EcoQoS。</summary>
    Idle = 0x40,

    /// <summary>BELOW_NORMAL_PRIORITY_CLASS（0x4000）。</summary>
    BelowNormal = 0x4000,

    /// <summary>NORMAL_PRIORITY_CLASS（0x20）。</summary>
    Normal = 0x20,

    /// <summary>ABOVE_NORMAL_PRIORITY_CLASS（0x8000）。</summary>
    AboveNormal = 0x8000,

    /// <summary>HIGH_PRIORITY_CLASS（0x80）。</summary>
    High = 0x80,

    /// <summary>REALTIME_PRIORITY_CLASS（0x100）。警告：实时优先级可能导致系统不稳定，规则应慎用。</summary>
    Realtime = 0x100,
}

/// <summary>
/// tweak 动作抽象基类（record，架构 3.1）。
/// 扩展方式（开闭原则）：新增动作类型 = 新增 record + 注册对应 <c>ITweakActionHandler</c>，
/// TweakEngine 不需任何修改。
/// </summary>
public abstract record TweakAction;

/// <summary>注册表值写入。</summary>
/// <param name="Path">注册表路径，如 <c>HKCU\Software\pcopt</c> 或 <c>HKLM\SOFTWARE\...</c>。</param>
/// <param name="Name">值名称。</param>
/// <param name="Value">目标值（DWord→int、QWord→long、Binary→byte[]、MultiString→string[]、字符串→string）。</param>
/// <param name="Kind">值类型。</param>
public sealed record RegistrySetAction(string Path, string Name, object Value, RegistryValueKind Kind) : TweakAction;

/// <summary>服务启动类型修改（SCM ChangeServiceConfigW）。</summary>
public sealed record ServiceStartTypeAction(string ServiceName, ServiceStartMode Target) : TweakAction;

/// <summary>计划任务启用 / 禁用。</summary>
public sealed record ScheduledTaskAction(string TaskPath, bool Enable) : TweakAction;

/// <summary>电源计划操作（powercfg）。</summary>
public sealed record PowerCfgAction(PowerActionKind Kind, string Argument) : TweakAction;

/// <summary>进程优先级 / 亲和性调整（按进程名匹配第一个实例）。</summary>
public sealed record ProcessPriorityAction(string ProcessName, ProcessPriorityClass Priority, long? AffinityMask) : TweakAction;

/// <summary>计时器分辨率请求（NtSetTimerResolution，100ns 单位；0.5ms = 5000）。</summary>
public sealed record TimerResolutionAction(uint DesiredResolution100Ns) : TweakAction;

/// <summary>Shell 命令执行（Process.Start 封装；不可自动还原，Revert 返回明确失败）。</summary>
public sealed record ShellAction(string Command, string Arguments) : TweakAction;
