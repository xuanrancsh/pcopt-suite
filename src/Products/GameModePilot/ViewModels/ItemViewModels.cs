// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameModePilot.ViewModels;

/// <summary>
/// 调整项网格中的单项（PRD 2.6 / A-P1-2）：人话名 + 说明 + 风险标签 + 开关。
/// <see cref="IsEnabled"/> 绑定到组件库 TweakCard 的 IsApplied（启用即"已生效"观感）。
/// </summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly Func<TweakItemViewModel, Task>? _onApply;
    private readonly Func<TweakItemViewModel, Task>? _onRevert;

    /// <summary>初始化调整项。</summary>
    public TweakItemViewModel(
        string ruleId,
        string title,
        string description,
        RiskLevel risk,
        bool isEnabled,
        Func<TweakItemViewModel, Task>? onApply,
        Func<TweakItemViewModel, Task>? onRevert)
    {
        RuleId = ruleId;
        Title = title;
        Description = description;
        Risk = risk;
        _onApply = onApply;
        _onRevert = onRevert;
        IsEnabled = isEnabled;

        ApplyCommand = new AsyncRelayCommand(() => InvokeAsync(_onApply), () => _onApply is not null);
        RevertCommand = new AsyncRelayCommand(() => InvokeAsync(_onRevert), () => _onRevert is not null);
    }

    /// <summary>规则 Id。</summary>
    public string RuleId { get; }

    /// <summary>人话名。</summary>
    public string Title { get; }

    /// <summary>人话说明。</summary>
    public string Description { get; }

    /// <summary>风险等级。</summary>
    public RiskLevel Risk { get; }

    /// <summary>是否启用（关闭后会话不再应用该项）。</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>[应用]=启用该项。</summary>
    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>[还原]=关闭该项。</summary>
    public IAsyncRelayCommand RevertCommand { get; }

    private Task InvokeAsync(Func<TweakItemViewModel, Task>? action) =>
        action is null ? Task.CompletedTask : action(this);
}

/// <summary>游戏列表项（PRD 2.6 / A-P0-1）。</summary>
public sealed class GameListItemViewModel
{
    /// <summary>初始化游戏列表项。</summary>
    public GameListItemViewModel(string gameName, string platformText, string antiCheatText, string executablePath, bool isRunning)
    {
        GameName = gameName;
        PlatformText = platformText;
        AntiCheatText = antiCheatText;
        ExecutablePath = executablePath;
        IsRunning = isRunning;
    }

    /// <summary>游戏名。</summary>
    public string GameName { get; }

    /// <summary>平台文案。</summary>
    public string PlatformText { get; }

    /// <summary>反作弊文案。</summary>
    public string AntiCheatText { get; }

    /// <summary>可执行路径。</summary>
    public string ExecutablePath { get; }

    /// <summary>是否运行中。</summary>
    public bool IsRunning { get; }
}

/// <summary>会话历史项（PRD 2.6"最近会话"）。</summary>
public sealed class SessionHistoryItemViewModel
{
    /// <summary>初始化会话历史项。</summary>
    public SessionHistoryItemViewModel(string title, string detail)
    {
        Title = title;
        Detail = detail;
    }

    /// <summary>标题（含游戏名）。</summary>
    public string Title { get; }

    /// <summary>明细（时间 · 时长 · 应用项数 · 还原状态）。</summary>
    public string Detail { get; }
}
