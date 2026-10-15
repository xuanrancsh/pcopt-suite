// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 内置最小引擎（T36 / PRD 4.7-2）：产品 A 未安装 / 未运行时，"一键优化并复测"退化为
/// <b>电源计划 + 计时器分辨率 + 内存清理</b>三项内核已有能力（架构 4.3）。
/// <para>
/// 电源与计时器以一条动态规则经内核 <see cref="TweakEngine"/> 应用（先写 Journal、可 LIFO 还原）；
/// 内存清理为一次性动作（不产生持续改动，无需还原）。
/// </para>
/// </summary>
public sealed class MinimalOptimizationEngine
{
    /// <summary>规则 Id（分类.对象.动作）。</summary>
    public const string RuleId = "perf.minimal.gaming";

    /// <summary>Windows 高性能电源计划 GUID（内置）。</summary>
    public static readonly Guid HighPerformanceScheme = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    /// <summary>计时器分辨率 0.5ms（100ns 单位）。</summary>
    public const uint TimerResolutionHalfMs = 5000;

    private readonly TweakEngine _engine;
    private readonly IMemoryCleaner _memory;
    private readonly ILogger<MinimalOptimizationEngine> _logger;

    /// <summary>初始化最小引擎。</summary>
    public MinimalOptimizationEngine(TweakEngine engine, IMemoryCleaner memory, ILogger<MinimalOptimizationEngine> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>构造内置最小引擎规则（电源高性能 + 0.5ms 计时器）。</summary>
    public static TweakRule BuildRule() => new()
    {
        Id = RuleId,
        Category = "性能",
        Risk = RiskLevel.Safe,
        DisplayName = "游戏高性能组合（电源 + 计时器）",
        Description = "切换到高性能电源计划并锁定更精确的计时器，让 CPU 持续保持高频率。",
        ConsequenceHints = new[] { "耗电与发热会略增" },
        Actions = new TweakAction[]
        {
            new PowerCfgAction(PowerActionKind.SetActive, HighPerformanceScheme.ToString("D")),
            new TimerResolutionAction(TimerResolutionHalfMs),
        },
    };

    /// <summary>当前最小引擎是否已应用。</summary>
    public bool IsApplied => _engine.IsApplied(RuleId, ResourcePaths.ProductName);

    /// <summary>
    /// 应用最小引擎：电源 + 计时器（经内核引擎，可还原）+ 一次性内存清理。
    /// </summary>
    public Result<string> Apply()
    {
        var register = _engine.RegisterRule(BuildRule());
        if (!register.IsSuccess)
        {
            return Result<string>.Fail(register.Error);
        }

        var apply = _engine.ApplyRule(RuleId, ResourcePaths.ProductName);
        if (!apply.IsSuccess)
        {
            return Result<string>.Fail(apply.Error);
        }

        var memory = _memory.Purge();
        var memoryText = memory.IsSuccess
            ? $"内存清理已释放约 {Math.Round(memory.Value / 1024.0 / 1024.0)} MB"
            : $"内存清理已跳过（{memory.Error}）";

        _logger.LogInformation("内置最小引擎已应用：电源 + 计时器 + {Memory}", memoryText);
        return Result<string>.Ok($"已用内置最小引擎（电源 + 计时器 + 内存清理）。{memoryText}。");
    }

    /// <summary>还原最小引擎中的持久改动（电源 / 计时器）。</summary>
    public Result<string> Revert()
    {
        var revert = _engine.RevertRule(RuleId, ResourcePaths.ProductName);
        return revert.IsSuccess
            ? Result<string>.Ok("已还原电源计划与计时器设置。")
            : Result<string>.Fail(revert.Error);
    }
}
