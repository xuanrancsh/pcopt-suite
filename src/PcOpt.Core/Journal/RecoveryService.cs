// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;

namespace PcOpt.Core.Journal;

/// <summary>
/// 开机恢复报告（不可变记录）。
/// </summary>
/// <param name="SessionsRecovered">成功恢复并标记 <see cref="JournalStatus.Recovered"/> 的会话数。</param>
/// <param name="EntriesRestored">成功还原的条目数。</param>
/// <param name="FailedRuleIds">还原失败的规则 Id（去重，供 UI 提示用户）。</param>
/// <param name="RemainingOpenSessionIds">仍保持 Open 的会话 Id（下次启动重试）。</param>
public sealed record RecoveryReport(
    int SessionsRecovered,
    int EntriesRestored,
    IReadOnlyList<string> FailedRuleIds,
    IReadOnlyList<string> RemainingOpenSessionIds)
{
    /// <summary>是否全部恢复完成（无失败、无遗留）。</summary>
    public bool IsFullyRecovered => FailedRuleIds.Count == 0 && RemainingOpenSessionIds.Count == 0;
}

/// <summary>
/// 开机恢复服务（架构 3.2 / PRD A-P0-5）。
/// 启动时扫描全部 <see cref="JournalStatus.Open"/> 会话 → 按 LIFO 逐项恢复 →
/// 全部成功则标记 <see cref="JournalStatus.Recovered"/> 并通知用户；
/// 任一失败则该会话保持 Open（下次启动重试），确保"崩溃 / 断电后系统 100% 恢复原状"。
/// </summary>
public sealed class RecoveryService
{
    private readonly JournalStore _journalStore;
    private readonly IActionRestorer _restorer;
    private readonly ILogger<RecoveryService> _logger;

    /// <summary>初始化恢复服务。</summary>
    public RecoveryService(JournalStore journalStore, IActionRestorer restorer, ILogger<RecoveryService> logger)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _restorer = restorer ?? throw new ArgumentNullException(nameof(restorer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 扫描并恢复全部 Open 会话。
    /// 会话按开始时间升序处理；同一会话内条目按"应用时间倒序、动作序号倒序"（LIFO）恢复。
    /// </summary>
    public Result<RecoveryReport> RecoverOpenSessions()
    {
        // 关键路径：读还原日志失败必须显式失败——绝不把"读不到"当成"没有待恢复会话"而静默成功，
        // 那会让崩溃 / 断电后的系统"什么都没恢复"却报告已恢复（内核可还原承诺）。
        var sessionsResult = _journalStore.TryListSessions();
        if (!sessionsResult.IsSuccess)
        {
            _logger.LogError("开机恢复：读取还原日志失败：{Error}", sessionsResult.Error);
            return Result<RecoveryReport>.Fail($"无法读取还原日志，已中止恢复（下次启动重试）：{sessionsResult.Error}");
        }

        var openSessions = sessionsResult.Value!
            .Where(s => s.Status == JournalStatus.Open)
            .OrderBy(s => s.StartedAt)
            .ToList();

        if (openSessions.Count == 0)
        {
            return Result<RecoveryReport>.Ok(new RecoveryReport(0, 0, Array.Empty<string>(), Array.Empty<string>()));
        }

        var entriesRestored = 0;
        var failedRuleIds = new List<string>();
        var remainingOpenSessionIds = new List<string>();

        foreach (var session in openSessions)
        {
            // LIFO：最后应用的条目最先还原
            var pending = session.OpenEntries
                .OrderByDescending(e => e.AppliedAt)
                .ThenByDescending(e => e.ActionIndex)
                .ToList();

            var updatedEntries = new List<JournalEntry>(session.Entries);
            var sessionFailed = false;

            foreach (var entry in pending)
            {
                var result = _restorer.Restore(entry);
                if (result.IsSuccess)
                {
                    entriesRestored++;
                    var index = updatedEntries.FindIndex(e => ReferenceEquals(e, entry));
                    if (index >= 0)
                    {
                        updatedEntries[index] = entry.WithReverted(DateTime.UtcNow);
                    }
                }
                else
                {
                    sessionFailed = true;
                    if (!failedRuleIds.Contains(entry.RuleId))
                    {
                        failedRuleIds.Add(entry.RuleId);
                    }

                    _logger.LogError(
                        "开机还原失败（规则: {RuleId}，动作序号: {ActionIndex}）：{Error}",
                        entry.RuleId,
                        entry.ActionIndex,
                        result.Error);
                }
            }

            if (sessionFailed)
            {
                // 保持 Open：下次启动继续尝试，保证系统最终可恢复
                remainingOpenSessionIds.Add(session.SessionId);
                // 部分还原成功也要持久化 RevertedAt 标记，避免重启后重复还原已完成的条目
                // （未还原条目 RevertedAt 保持 null，下次启动自动重试）
                var stillOpen = new Journal(
                    session.SessionId,
                    session.Product,
                    session.StartedAt,
                    JournalStatus.Open,
                    updatedEntries);
                var partialSave = _journalStore.Save(stillOpen);
                if (!partialSave.IsSuccess)
                {
                    _logger.LogError(
                        "持久化会话 {SessionId} 的部分还原进度失败：{Error}",
                        session.SessionId,
                        partialSave.Error);
                }

                _logger.LogWarning("会话 {SessionId} 未完全恢复，保持 Open 待下次重试", session.SessionId);
            }
            else
            {
                var recovered = new Journal(
                    session.SessionId,
                    session.Product,
                    session.StartedAt,
                    JournalStatus.Recovered,
                    updatedEntries);
                var save = _journalStore.Save(recovered);
                if (!save.IsSuccess)
                {
                    remainingOpenSessionIds.Add(session.SessionId);
                    _logger.LogError("标记会话 {SessionId} 为已恢复失败：{Error}", session.SessionId, save.Error);
                }
                else
                {
                    _logger.LogInformation(
                        "会话 {SessionId} 已恢复（还原 {Count} 项）",
                        session.SessionId,
                        pending.Count);
                }
            }
        }

        var report = new RecoveryReport(
            openSessions.Count - remainingOpenSessionIds.Count,
            entriesRestored,
            failedRuleIds,
            remainingOpenSessionIds);
        return Result<RecoveryReport>.Ok(report);
    }
}
