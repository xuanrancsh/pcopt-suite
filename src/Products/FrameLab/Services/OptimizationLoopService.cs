// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>优化应用报告（T36）。</summary>
/// <param name="Channel">实际使用的通道（"GameModePilot" 或 "内置最小引擎"）。</param>
/// <param name="AppliedRuleIds">实际请求应用的规则 Id。</param>
/// <param name="Message">人话说明。</param>
public sealed record OptimizationApplyReport(string Channel, IReadOnlyList<string> AppliedRuleIds, string Message);

/// <summary>
/// 优化-复测闭环的"应用"环节（T36，PRD C-P0-5）：
/// 诊断建议 → 优先调用产品 A 的引擎（IPC <c>applyProfile</c>）；A 未运行则降级到内置最小引擎
/// （电源 + 计时器 + 内存清理）。之后由 UI 引导复测，再经 <see cref="ComparisonAnalyzer"/> 出结论。
/// </summary>
public sealed class OptimizationLoopService
{
    private readonly IGameModePilotClient _pilot;
    private readonly MinimalOptimizationEngine _minimal;
    private readonly ILogger<OptimizationLoopService> _logger;

    /// <summary>初始化闭环服务。</summary>
    public OptimizationLoopService(
        IGameModePilotClient pilot,
        MinimalOptimizationEngine minimal,
        ILogger<OptimizationLoopService> logger)
    {
        _pilot = pilot ?? throw new ArgumentNullException(nameof(pilot));
        _minimal = minimal ?? throw new ArgumentNullException(nameof(minimal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>产品 A 是否可用（用于 UI 提示通道）。</summary>
    public bool IsPilotAvailable => _pilot.IsRunning();

    /// <summary>内置最小引擎当前是否已应用（退出前提示还原 / UI 状态用）。</summary>
    public bool IsMinimalApplied => _minimal.IsApplied;

    /// <summary>
    /// 应用诊断建议对应的优化。
    /// </summary>
    /// <param name="diagnoses">诊断结果（来自 <see cref="Diagnoser"/>）。</param>
    public Result<OptimizationApplyReport> Apply(IReadOnlyList<Diagnosis> diagnoses)
    {
        ArgumentNullException.ThrowIfNull(diagnoses);

        if (diagnoses.Count == 0)
        {
            return Result<OptimizationApplyReport>.Fail("没有需要应用的优化建议");
        }

        var ruleIds = diagnoses
            .SelectMany(d => d.RuleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var needsPilot = diagnoses.Any(d =>
            d.RecommendationId is RecommendationIds.InterruptAffinity or RecommendationIds.GpuBottleneck);
        var hasMinimal = diagnoses.Any(d =>
            d.RecommendationId is RecommendationIds.MemoryCleanup or RecommendationIds.PowerPlan);

        // 首选：产品 A 的引擎（可处理全部建议）
        if (_pilot.IsRunning())
        {
            var apply = _pilot.ApplyProfile(ruleIds);
            if (apply.IsSuccess)
            {
                _logger.LogInformation("已通过 GameModePilot 应用 {Count} 条规则", ruleIds.Count);
                return Result<OptimizationApplyReport>.Ok(new OptimizationApplyReport(
                    "GameModePilot",
                    ruleIds,
                    "已通过「游戏模式」应用优化，现在请回到游戏复测一次。"));
            }

            _logger.LogWarning("调用 GameModePilot 失败，降级到内置最小引擎：{Error}", apply.Error);
        }

        // 降级：内置最小引擎（电源 + 计时器 + 内存清理）
        if (hasMinimal)
        {
            var minimal = _minimal.Apply();
            if (!minimal.IsSuccess)
            {
                return Result<OptimizationApplyReport>.Fail(minimal.Error);
            }

            return Result<OptimizationApplyReport>.Ok(new OptimizationApplyReport(
                "内置最小引擎",
                new[] { MinimalOptimizationEngine.RuleId },
                minimal.Value + " 现在请回到游戏复测一次。"));
        }

        if (needsPilot)
        {
            return Result<OptimizationApplyReport>.Fail(
                "这些建议需要「游戏模式（GameMode Pilot）」才能自动应用，请先安装并启动它，再回来一键优化。");
        }

        return Result<OptimizationApplyReport>.Fail("当前没有可自动应用的优化项");
    }

    /// <summary>还原本次优化（A 可用走 A；否则还原最小引擎）。</summary>
    public Result<string> Revert()
    {
        if (_pilot.IsRunning())
        {
            var revert = _pilot.RevertAll();
            if (revert.IsSuccess)
            {
                return revert;
            }

            _logger.LogWarning("调用 GameModePilot 还原失败：{Error}", revert.Error);
        }

        return _minimal.Revert();
    }
}
