// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Tweak;

/// <summary>
/// 官方签名规则包内容策略（PRD 合规红线 #9，2026-10-14 用户确认）。
/// 官方签名包只允许"可自动还原"的动作；Shell 命令 / powercfg 删除计划等
/// 退出后无法自动恢复的动作不得出现在官方包中（用户自定义未签名规则允许，
/// 但 UI 必须标注"此项退出后不会自动还原"且应用前走 ConfirmDialog）。
/// </summary>
public static class OfficialRulePolicy
{
    /// <summary>
    /// 判断动作是否可自动还原（与各 handler 的 Revert 实现语义一致）：
    /// <list type="bullet">
    /// <item><see cref="ShellAction"/>：Revert 恒失败（命令副作用不可逆记录）→ 不可还原；</item>
    /// <item><see cref="PowerCfgAction"/> 且 <see cref="PowerActionKind.Delete"/>：
    /// 已删除的电源方案无法自动重建 → 不可还原（List/Import/SetActive 均有配对还原实现）；</item>
    /// <item>其余动作（注册表 / 服务 / 计划任务 / 进程优先级 / 计时器分辨率）→ 可还原。</item>
    /// </list>
    /// </summary>
    public static bool IsAutoRevertible(TweakAction action) => action switch
    {
        ShellAction => false,
        PowerCfgAction power => power.Kind != PowerActionKind.Delete,
        _ => true,
    };

    /// <summary>动作不可还原时的人话原因（UI 标注与错误信息共用）。</summary>
    public static string DescribeNonRevertible(TweakAction action) => action switch
    {
        ShellAction shell => $"Shell 命令“{shell.Command}”无法自动还原",
        PowerCfgAction power => $"电源计划操作 {power.Kind}（删除电源方案）无法自动还原",
        _ => string.Empty,
    };

    /// <summary>
    /// 校验单条规则是否满足官方包策略。
    /// 成功 = 规则只含可自动还原的动作；失败 = 错误信息列出全部违规动作（中文人话）。
    /// </summary>
    public static Result ValidateRule(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return ValidateActions(rule.Id, rule.Actions);
    }

    /// <summary>
    /// 校验整包（多条规则）。首条违规即失败并汇总全部违规项，便于规则作者一次修完。
    /// </summary>
    public static Result ValidatePackage(IEnumerable<TweakRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var violations = new List<string>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            foreach (var action in rule.Actions)
            {
                if (!IsAutoRevertible(action))
                {
                    violations.Add($"规则“{rule.Id}”包含不可自动还原的动作：{DescribeNonRevertible(action)}");
                }
            }
        }

        if (violations.Count > 0)
        {
            return Result.Fail(
                $"官方签名规则包只能使用可自动还原的动作（合规红线 #9）：{string.Join("；", violations)}。" +
                "如确需使用此类动作，请以自定义规则（未签名）形式提供，界面将标注“退出后不会自动还原”。");
        }

        return Result.Ok();
    }

    private static Result ValidateActions(string ruleId, IReadOnlyList<TweakAction> actions)
    {
        var violations = actions
            .Where(action => !IsAutoRevertible(action))
            .Select(DescribeNonRevertible)
            .ToArray();

        if (violations.Length > 0)
        {
            return Result.Fail(
                $"规则“{ruleId}”包含不可自动还原的动作（合规红线 #9）：{string.Join("；", violations)}。" +
                "官方签名规则包只能使用可自动还原的动作；如确需使用，请以自定义规则（未签名）形式提供。");
        }

        return Result.Ok();
    }
}
