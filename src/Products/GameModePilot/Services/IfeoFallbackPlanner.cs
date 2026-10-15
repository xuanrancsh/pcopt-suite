// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Win32;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// IFEO 降级方案规划器（T13，PRD A-P0-6 / 红线 #5，架构 4.1）。
/// 命中内核级反作弊（Vanguard / EAC / BattlEye / RICHOCHET / 腾讯 ACE）时，
/// 进程调度类操作（<see cref="ProcessPriorityAction"/>）改为写
/// <c>HKLM\...\Image File Execution Options\{exe}\PerfOptions\CpuPriorityClass</c>——
/// 让系统在进程创建时就按配置设置优先级，避免运行期 <c>SetPriorityClass</c> 触发反作弊误判。
/// 设计要点：
/// <list type="bullet">
/// <item>IFEO 本质是注册表写入，复用内核 <c>RegistrySetActionHandler</c> 的快照 / 还原能力，
/// 不新增 handler（审计面集中，架构 2 依赖规则 #4）；</item>
/// <item>IFEO 只能表达 CPU 优先级，无法表达亲和性掩码，故降级时忽略 <c>AffinityMask</c>；</item>
/// <item>降级后的规则 Id 追加 <see cref="IfeoSuffix"/> 后缀，与原规则区分，便于 Journal 审计。</item>
/// </list>
/// </summary>
public static class IfeoFallbackPlanner
{
    /// <summary>IFEO 降级规则 Id 后缀。</summary>
    public const string IfeoSuffix = ".ifeo";

    /// <summary>IFEO PerfOptions 的 CpuPriorityClass 值名称。</summary>
    public const string CpuPriorityClassValueName = "CpuPriorityClass";

    /// <summary>规则是否含进程调度类动作（决定是否需要 IFEO 降级）。</summary>
    public static bool ContainsProcessSchedulingAction(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Actions.Any(action => action is ProcessPriorityAction);
    }

    /// <summary>
    /// 进程优先级 → IFEO <c>CpuPriorityClass</c> 编码（Win32 优先级 1-6，默认 3=Normal）。
    /// </summary>
    public static int MapCpuPriorityClass(ProcessPriorityClass priority) => priority switch
    {
        ProcessPriorityClass.Idle => IfeoPaths.CpuPriorityClassValues.Idle,
        ProcessPriorityClass.BelowNormal => IfeoPaths.CpuPriorityClassValues.BelowNormal,
        ProcessPriorityClass.Normal => IfeoPaths.CpuPriorityClassValues.Normal,
        ProcessPriorityClass.AboveNormal => IfeoPaths.CpuPriorityClassValues.AboveNormal,
        ProcessPriorityClass.High => IfeoPaths.CpuPriorityClassValues.High,
        ProcessPriorityClass.Realtime => IfeoPaths.CpuPriorityClassValues.Realtime,
        _ => IfeoPaths.CpuPriorityClassValues.Normal,
    };

    /// <summary>
    /// 构造 IFEO 降级规则：把每个进程调度类动作替换为对 IFEO PerfOptions 的注册表写入。
    /// 不含进程调度类动作时原样返回（幂等，避免无谓改写）。
    /// </summary>
    public static TweakRule BuildIfeoRule(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!ContainsProcessSchedulingAction(rule))
        {
            return rule;
        }

        var actions = rule.Actions.Select(TransformAction).ToArray();
        return new TweakRule
        {
            Id = rule.Id + IfeoSuffix,
            Category = rule.Category,
            Risk = rule.Risk,
            DisplayName = rule.DisplayName,
            Description = rule.Description,
            ConsequenceHints = rule.ConsequenceHints,
            Conditions = rule.Conditions,
            Actions = actions,
        };
    }

    /// <summary>单动作转换：进程调度 → IFEO 注册表写入；其余保持不变。</summary>
    private static TweakAction TransformAction(TweakAction action) => action switch
    {
        ProcessPriorityAction process => new RegistrySetAction(
            IfeoPaths.PerfOptions(process.ProcessName),
            CpuPriorityClassValueName,
            MapCpuPriorityClass(process.Priority),
            RegistryValueKind.DWord),
        _ => action,
    };
}
