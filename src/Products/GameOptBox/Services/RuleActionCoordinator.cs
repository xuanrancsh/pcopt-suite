// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 规则应用 / 还原协调器：集中处理"危险项二次确认 + 应用 + Toast 反馈"（PRD 5.5）。
/// 供规则浏览、延迟优化、显卡调优等多个页面复用，避免各处重复实现确认逻辑。
/// </summary>
public sealed class RuleActionCoordinator
{
    private readonly RuleRepository _rules;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    /// <summary>初始化协调器。</summary>
    public RuleActionCoordinator(
        RuleRepository rules,
        IDialogService dialogs,
        INotificationService notifications)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>
    /// 应用规则：危险项先走二次确认（关闭 Defender / 更新 / 内核隔离需输入"我了解风险"，PRD 5.5）。
    /// </summary>
    /// <returns>是否最终应用成功。</returns>
    public Task<bool> ApplyAsync(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.Risk == RiskLevel.Danger)
        {
            var highRisk = IsHighRisk(rule);
            var consequence = rule.ConsequenceHints.Length > 0
                ? rule.ConsequenceHints
                : new[] { "此改动影响较大" };
            var decision = _dialogs.Confirm(
                "危险操作确认",
                $"即将应用“{rule.DisplayName}”。",
                consequence,
                highRisk);

            if (!decision.Confirmed)
            {
                _notifications.Notify("已取消本次操作");
                return Task.FromResult(false);
            }
        }

        var result = _rules.Apply(rule.Id);
        if (!result.IsSuccess)
        {
            _notifications.Notify($"应用失败：{result.Error}");
            return Task.FromResult(false);
        }

        _notifications.Notify(result.Value!.AlreadyApplied
            ? $"“{rule.DisplayName}”已经是生效状态"
            : $"已应用：{rule.DisplayName}，可在卡片点“还原”撤回");
        return Task.FromResult(true);
    }

    /// <summary>还原规则。</summary>
    /// <returns>是否最终还原成功。</returns>
    public Task<bool> RevertAsync(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var result = _rules.Revert(rule.Id);
        if (!result.IsSuccess)
        {
            _notifications.Notify($"还原失败：{result.Error}");
            return Task.FromResult(false);
        }

        _notifications.Notify($"已还原：{rule.DisplayName}，你的电脑恢复原样了");
        return Task.FromResult(true);
    }

    /// <summary>
    /// 是否为高危规则（关闭 Defender / 自动更新 / 内核隔离）：除危险确认外还需输入"我了解风险"（PRD 5.5）。
    /// </summary>
    public static bool IsHighRisk(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Risk == RiskLevel.Danger
            && (Contains(rule.Id, "defender")
                || Contains(rule.Id, "windowsupdate")
                || Contains(rule.Id, "coreisolation")
                || Contains(rule.Id, "smartscreen"));
    }

    private static bool Contains(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);
}
