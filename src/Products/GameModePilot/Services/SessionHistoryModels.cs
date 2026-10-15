// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 会话历史记录（T15 最近会话；PRD A-P0-5 / 2.6 线框"最近会话"）。
/// 与内核还原日志（Journal）互补：Journal 记录"改了什么"，本记录记录"哪次游戏、多久、用了多少项"。
/// 不含任何用户标识信息（架构 8.3）。
/// </summary>
/// <param name="SessionId">会话 Id（与 Journal 对应）。</param>
/// <param name="GameId">游戏 Id。</param>
/// <param name="GameName">人话游戏名。</param>
/// <param name="StartedAtUtc">开始时间（UTC）。</param>
/// <param name="EndedAtUtc">结束时间（UTC）；进行中为 null。</param>
/// <param name="AppliedCount">应用项数。</param>
/// <param name="RevertedCount">还原动作条目数。</param>
/// <param name="AntiCheat">反作弊画像（字符串形式，便于展示"为何降级"）。</param>
/// <param name="FullyReverted">是否全部还原。</param>
public sealed record SessionRecord(
    string SessionId,
    string GameId,
    string GameName,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    int AppliedCount,
    int RevertedCount,
    string AntiCheat,
    bool FullyReverted);
