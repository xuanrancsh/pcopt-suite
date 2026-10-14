// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Tweak;

/// <summary>
/// tweak 规则（架构 3.1）。
/// 规则 Id 命名规范：<c>分类.对象.动作</c>（如 <c>svc.sysmain.disable</c>），分类 ∈ 性能|游戏|延迟|网络|隐私|体验。
/// </summary>
public sealed class TweakRule
{
    /// <summary>规则 Id（分类.对象.动作）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>分类：性能|游戏|延迟|网络|隐私|体验。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>风险等级。</summary>
    public RiskLevel Risk { get; init; }

    /// <summary>人话名（动词+结果，如"让游戏少抢 CPU 的后台服务"）。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>人话说明。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>后果提示（≤3 条，PRD 5.3 不恐吓原则）。</summary>
    public string[] ConsequenceHints { get; init; } = Array.Empty<string>();

    /// <summary>系统适配条件（全部满足才应用；空 = 不限）。</summary>
    public OsCondition[] Conditions { get; init; } = Array.Empty<OsCondition>();

    /// <summary>有序动作列表（按序应用，还原时 LIFO）。</summary>
    public TweakAction[] Actions { get; init; } = Array.Empty<TweakAction>();
}
