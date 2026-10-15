// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>某系统服务的人话说明与推荐启动类型（服务知识库条目）。</summary>
/// <param name="Description">人话说明（PRD 5.3 说人话规范）。</param>
/// <param name="Recommended">推荐启动类型。</param>
/// <param name="NonEssential">是否属于"用不到可关闭"的非必要服务。</param>
public sealed record ServiceKnowledge(string Description, ServiceStartMode Recommended, bool NonEssential);

/// <summary>
/// 系统服务目录条目（T22，PRD B-P0-1：200+ 服务列表）。
/// </summary>
/// <param name="Name">服务名。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="CurrentStart">当前启动类型。</param>
/// <param name="RecommendedStart">推荐启动类型。</param>
/// <param name="NonEssential">是否非必要（可关闭）。</param>
/// <param name="Description">人话说明。</param>
public sealed record ServiceEntry(
    string Name,
    string DisplayName,
    ServiceStartMode CurrentStart,
    ServiceStartMode RecommendedStart,
    bool NonEssential,
    string Description);

/// <summary>一键"游戏模式"服务优化方案。</summary>
/// <param name="Targets">将被改为手动 / 禁用的服务。</param>
/// <param name="Rule">方案规则（可直接注册进引擎并 apply/revert）。</param>
public sealed record ServiceOptimizationPlan(IReadOnlyList<ServiceEntry> Targets, TweakRule Rule);
