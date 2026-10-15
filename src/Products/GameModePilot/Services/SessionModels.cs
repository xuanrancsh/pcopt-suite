// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Config;
using PcOpt.Core.Game;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 会话阶段（PRD 5.4 三态的具体化）。
/// </summary>
public enum SessionPhase
{
    /// <summary>🛡️ 守护中（无会话）。</summary>
    Guarding,

    /// <summary>正在应用优化（进行中，琥珀）。</summary>
    Starting,

    /// <summary>🎮 游戏中（已应用，绿）。</summary>
    Playing,

    /// <summary>正在还原（进行中，琥珀）。</summary>
    Reverting,

    /// <summary>✅ 已还原（灰）。</summary>
    Restored,
}

/// <summary>会话进度（PRD 5.4：超过 2 秒的操作必须报进度）。</summary>
/// <param name="Message">人话进度文案（"正在切换电源计划… 2/6"）。</param>
/// <param name="Current">当前步骤序号（1 起）。</param>
/// <param name="Total">总步骤数。</param>
public sealed record SessionProgress(string Message, int Current, int Total);

/// <summary>单条规则的处理结果。</summary>
/// <param name="RuleId">规则 Id。</param>
/// <param name="DisplayName">人话名。</param>
/// <param name="Applied">本次是否真实应用。</param>
/// <param name="Skipped">是否因幂等（已应用）跳过。</param>
/// <param name="UsedIfeoFallback">是否走 IFEO 降级方案（内核级反作弊命中时的进程调度类操作）。</param>
/// <param name="Error">失败原因；成功为 null。</param>
public sealed record RuleOutcome(
    string RuleId,
    string DisplayName,
    bool Applied,
    bool Skipped,
    bool UsedIfeoFallback,
    string? Error);

/// <summary>
/// 会话请求（游戏启动时构造，架构 4.1）。
/// </summary>
/// <param name="GameId">游戏 Id。</param>
/// <param name="GameName">人话游戏名。</param>
/// <param name="ExecutableNames">可执行文件名集合（构造进程调度类规则用）。</param>
/// <param name="Intensity">优化强度档（PRD A-P1-1）。</param>
/// <param name="DisabledRuleIds">用户逐项关闭的规则 Id（A-P1-2）。</param>
public sealed record SessionRequest(
    string GameId,
    string GameName,
    IReadOnlyList<string> ExecutableNames,
    ProfileIntensity Intensity,
    IReadOnlyList<string> DisabledRuleIds)
{
    /// <summary>用默认强度与无禁用项构造请求。</summary>
    public static SessionRequest Create(string gameId, string gameName, IReadOnlyList<string> executableNames) =>
        new(gameId, gameName, executableNames, ProfileIntensity.Balanced, Array.Empty<string>());
}

/// <summary>会话开始结果。</summary>
/// <param name="Success">是否开始成功。</param>
/// <param name="GameId">游戏 Id。</param>
/// <param name="GameName">人话游戏名。</param>
/// <param name="AntiCheat">反作弊画像。</param>
/// <param name="AntiCheatProduct">命中的反作弊产品。</param>
/// <param name="UsedIfeoFallback">是否整体走 IFEO 降级方案。</param>
/// <param name="Outcomes">各规则处理结果。</param>
/// <param name="Error">整体失败原因；成功为 null。</param>
public sealed record SessionStartResult(
    bool Success,
    string GameId,
    string GameName,
    AntiCheatProfile AntiCheat,
    KnownAntiCheat AntiCheatProduct,
    bool UsedIfeoFallback,
    IReadOnlyList<RuleOutcome> Outcomes,
    string? Error)
{
    /// <summary>成功应用（或已应用跳过）的项数。</summary>
    public int AppliedCount => Outcomes.Count(outcome => outcome.Applied || outcome.Skipped);

    /// <summary>真实执行的项数（不含幂等跳过）。</summary>
    public int ExecutedCount => Outcomes.Count(outcome => outcome.Applied);

    /// <summary>失败项数。</summary>
    public int FailedCount => Outcomes.Count(outcome => outcome.Error is not null);
}

/// <summary>会话结束（还原）结果。</summary>
/// <param name="Success">是否全部还原成功。</param>
/// <param name="RevertedCount">成功还原的动作条目数。</param>
/// <param name="FullyReverted">是否全部还原。</param>
/// <param name="FailedRuleIds">还原失败的规则 Id。</param>
/// <param name="Error">失败说明；成功为 null。</param>
public sealed record SessionEndResult(
    bool Success,
    int RevertedCount,
    bool FullyReverted,
    IReadOnlyList<string> FailedRuleIds,
    string? Error);

/// <summary>还原后校验结果。</summary>
/// <param name="Success">是否无残留。</param>
/// <param name="Failures">仍处于"已应用"状态的规则 Id。</param>
public sealed record SessionVerification(bool Success, IReadOnlyList<string> Failures);

/// <summary>会话状态快照（供 UI 三态卡与 IPC status 消费）。</summary>
/// <param name="Phase">当前阶段。</param>
/// <param name="GameName">当前游戏名；无会话为 null。</param>
/// <param name="AppliedCount">已应用项数。</param>
/// <param name="RevertedCount">已还原项数。</param>
/// <param name="AntiCheat">当前反作弊画像。</param>
public sealed record SessionSnapshot(
    SessionPhase Phase,
    string? GameName,
    int AppliedCount,
    int RevertedCount,
    AntiCheatProfile AntiCheat);
