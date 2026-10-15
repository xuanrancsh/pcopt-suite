// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 单条 tweak 规则卡片视图模型（T21，PRD 3.6 卡片 / 5.4 三态）。
/// 状态：未应用（应用按钮）/ 已应用（绿色边框 + 还原按钮）/ 危险（红框 + 应用需确认）。
/// </summary>
public sealed partial class RuleItemViewModel : ObservableObject
{
    private readonly RuleActionCoordinator _coordinator;

    /// <summary>初始化卡片视图模型。</summary>
    /// <param name="rule">规则。</param>
    /// <param name="isApplied">是否已应用。</param>
    /// <param name="coordinator">应用 / 还原协调器。</param>
    public RuleItemViewModel(TweakRule rule, bool isApplied, RuleActionCoordinator coordinator)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _isApplied = isApplied;

        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsApplied);
        RevertCommand = new AsyncRelayCommand(RevertAsync, () => IsApplied);
    }

    /// <summary>底层规则。</summary>
    public TweakRule Rule { get; }

    /// <summary>规则 Id。</summary>
    public string RuleId => Rule.Id;

    /// <summary>人话名。</summary>
    public string Title => Rule.DisplayName;

    /// <summary>人话说明。</summary>
    public string Description => Rule.Description;

    /// <summary>分类。</summary>
    public string Category => Rule.Category;

    /// <summary>风险等级。</summary>
    public RiskLevel Risk => Rule.Risk;

    /// <summary>后果提示（人话，用于卡片第二行）。</summary>
    public string ConsequenceText => Rule.ConsequenceHints.Length > 0
        ? "⚠️ " + string.Join("；", Rule.ConsequenceHints)
        : string.Empty;

    /// <summary>是否已应用（驱动卡片三态与按钮文案）。</summary>
    [ObservableProperty]
    private bool _isApplied;

    /// <summary>应用命令。</summary>
    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>还原命令。</summary>
    public IAsyncRelayCommand RevertCommand { get; }

    /// <summary>状态变化时刷新命令可执行性。</summary>
    partial void OnIsAppliedChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync()
    {
        if (await _coordinator.ApplyAsync(Rule))
        {
            IsApplied = true;
        }
    }

    private async Task RevertAsync()
    {
        if (await _coordinator.RevertAsync(Rule))
        {
            IsApplied = false;
        }
    }
}
