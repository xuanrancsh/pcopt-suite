// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 一键"游戏模式"服务优化方案构造器（T22，PRD B-P0-1 / 3.6）。
/// 纯函数：从服务列表中挑出"非必要 + 当前自动启动"的服务，生成一条可 apply/revert 的规则
/// （全部为 <see cref="ServiceStartTypeAction"/> 可自动还原动作，合规红线 #9）。
/// </summary>
public static class ServiceOptimizationPlanner
{
    /// <summary>方案的规则 Id。</summary>
    public const string PlanRuleId = "service.plan.gamemode";

    /// <summary>从服务列表挑选一键方案的目标服务。</summary>
    public static IReadOnlyList<ServiceEntry> SelectTargets(IEnumerable<ServiceEntry> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .Where(service => service.NonEssential)
            .Where(service => service.CurrentStart != service.RecommendedStart)
            .Where(service => service.RecommendedStart is ServiceStartMode.Manual or ServiceStartMode.Disabled)
            .ToList();
    }

    /// <summary>构造一键方案（含目标服务与可直接注册进引擎的规则）。</summary>
    public static ServiceOptimizationPlan Plan(IEnumerable<ServiceEntry> services)
    {
        var targets = SelectTargets(services);
        return new ServiceOptimizationPlan(targets, BuildRule(targets));
    }

    /// <summary>由目标服务构造规则。</summary>
    public static TweakRule BuildRule(IReadOnlyList<ServiceEntry> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var actions = targets
            .Select(target => (TweakAction)new ServiceStartTypeAction(target.Name, target.RecommendedStart))
            .ToArray();

        return new TweakRule
        {
            Id = PlanRuleId,
            Category = "性能",
            Risk = RiskLevel.Notice,
            DisplayName = "一键瘦身：关闭用不到的后台服务",
            Description = "把用不到的后台服务改为按需启动，游戏时更少抢 CPU，开机也更快。全部可一键还原。",
            ConsequenceHints = new[]
            {
                "个别功能（如蓝牙、打印、系统搜索）首次使用会稍慢",
                "退出或点“还原”后会自动恢复",
            },
            Conditions = Array.Empty<OsCondition>(),
            Actions = actions,
        };
    }
}
