// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Config;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 内置优化规则集构造器（T15 调整项网格 / A-P0-3 优化会话）。
/// 按"游戏 + 强度档"动态构造规则：
/// <list type="bullet">
/// <item>Conservative：仅电源计划 + 计时器分辨率（低风险、稳定优先，PRD A-P1-1）；</item>
/// <item>Balanced：增加游戏进程优先级；</item>
/// <item>Aggressive：再增加后台程序降级（EcoQoS）。</item>
/// </list>
/// 全部动作均为内核已实现且可自动还原的 handler（合规红线 #7 / #9），无 Shell / powercfg 删除动作。
/// 进程调度类动作（<see cref="ProcessPriorityAction"/>）在命中内核级反作弊时由
/// <see cref="IfeoFallbackPlanner"/> 自动降级为 IFEO 注册表方案（红线 #5）。
/// </summary>
public static class RuleSetBuilder
{
    /// <summary>电源计划规则 Id。</summary>
    public const string PowerRuleId = "game.power.activate";

    /// <summary>计时器分辨率规则 Id。</summary>
    public const string TimerRuleId = "game.timer.resolution";

    /// <summary>游戏进程优先级规则 Id。</summary>
    public const string GamePriorityRuleId = "game.exe.priority";

    /// <summary>后台程序降级规则 Id。</summary>
    public const string BackgroundPriorityRuleId = "game.bg.ecooqs";

    /// <summary>Windows"高性能"电源计划 GUID（系统内置）。</summary>
    private const string HighPerformancePowerSchemeGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    /// <summary>请求的强度档下适用的规则集合（有序：低风险在前）。</summary>
    public static IReadOnlyList<TweakRule> Build(SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rules = new List<TweakRule>
        {
            BuildPowerRule(),
            BuildTimerRule(),
        };

        var executable = ResolveExecutable(request);
        if (request.Intensity != ProfileIntensity.Conservative && executable is not null)
        {
            rules.Add(BuildGamePriorityRule(executable));
        }

        if (request.Intensity == ProfileIntensity.Aggressive)
        {
            rules.Add(BuildBackgroundPriorityRule());
        }

        return rules;
    }

    /// <summary>全部内置规则的元数据（供"调整项网格"展示，不依赖具体游戏）。</summary>
    public static IReadOnlyList<TweakRule> DescribeAll()
    {
        var placeholder = BuildGamePriorityRule("game.exe");
        return new[]
        {
            BuildPowerRule(),
            BuildTimerRule(),
            placeholder,
            BuildBackgroundPriorityRule(),
        };
    }

    /// <summary>电源计划：切到"高性能"（退出后由 Journal 还原原方案）。</summary>
    public static TweakRule BuildPowerRule() => new()
    {
        Id = PowerRuleId,
        Category = "游戏",
        Risk = RiskLevel.Safe,
        DisplayName = "切换到高性能电源计划",
        Description = "让 CPU 在游戏时更少降频。笔记本的电量消耗会快一些。",
        ConsequenceHints = new[] { "笔记本续航会短一些", "退出游戏后会自动切回原来的电源计划" },
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new PowerCfgAction(PowerActionKind.SetActive, HighPerformancePowerSchemeGuid) },
    };

    /// <summary>计时器分辨率：请求 0.5ms（退出后由 Journal 还原原值）。</summary>
    public static TweakRule BuildTimerRule() => new()
    {
        Id = TimerRuleId,
        Category = "游戏",
        Risk = RiskLevel.Notice,
        DisplayName = "把系统计时器精度调到 0.5 毫秒",
        Description = "让画面更跟手、操作延迟更低。占用极少，退出游戏后自动还原。",
        ConsequenceHints = new[] { "个别老程序对高精度计时器敏感", "退出游戏后会自动还原" },
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new TimerResolutionAction(5000) },
    };

    /// <summary>游戏进程优先级：提到 High（内核反作弊命中时降级 IFEO）。</summary>
    public static TweakRule BuildGamePriorityRule(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        return new TweakRule
        {
            Id = GamePriorityRuleId,
            Category = "游戏",
            Risk = RiskLevel.Notice,
            DisplayName = "给游戏进程更高的调度优先级",
            Description = "让游戏更优先拿到 CPU 时间，减少卡顿。退出游戏后自动还原。",
            ConsequenceHints = new[] { "后台程序可能稍有变慢", "退出游戏后会自动还原" },
            Conditions = Array.Empty<OsCondition>(),
            Actions = new TweakAction[] { new ProcessPriorityAction(executableName, ProcessPriorityClass.High, null) },
        };
    }

    /// <summary>后台程序降级：把资源管理器降到 Idle 并启用 EcoQoS（省电降温）。</summary>
    public static TweakRule BuildBackgroundPriorityRule() => new()
    {
        Id = BackgroundPriorityRuleId,
        Category = "游戏",
        Risk = RiskLevel.Notice,
        DisplayName = "让后台程序少抢资源",
        Description = "把资源管理器降到最低优先级并开启能效模式，把资源让给游戏。",
        ConsequenceHints = new[] { "桌面操作可能略微变慢", "退出游戏后会自动还原" },
        Conditions = Array.Empty<OsCondition>(),
        Actions = new TweakAction[] { new ProcessPriorityAction("explorer", ProcessPriorityClass.Idle, null) },
    };

    /// <summary>取首个可用的可执行文件名。</summary>
    private static string? ResolveExecutable(SessionRequest request) =>
        request.ExecutableNames.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
}
