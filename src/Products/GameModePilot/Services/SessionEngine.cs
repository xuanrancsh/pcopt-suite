// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Game;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 游戏优化会话引擎（T13 核心，架构 4.1 游戏会话时序）。
/// 职责：会话编排 <c>BeginSession → Apply → Verify → EndSession → Revert</c>，
/// 复用内核 <see cref="TweakEngine"/>（先写 Journal 后执行 + 幂等 + LIFO 还原）与
/// <see cref="JournalStore"/>；命中内核级反作弊时对进程调度类规则自动走
/// <see cref="IfeoFallbackPlanner"/> 降级方案（红线 #5）。
/// 线程模型：状态读写在 <see cref="_gate"/> 下串行；事件在锁外触发（避免回调重入死锁）。
/// </summary>
public sealed class SessionEngine
{
    /// <summary>产品名（还原日志归属，架构 8.1）。</summary>
    public const string ProductName = "GameModePilot";

    private readonly TweakEngine _engine;
    private readonly IAntiCheatDetector _antiCheat;
    private readonly JournalStore _journalStore;
    private readonly SessionHistoryStore _history;
    private readonly Func<SessionRequest, IReadOnlyList<TweakRule>> _ruleFactory;
    private readonly ILogger<SessionEngine> _logger;
    private readonly object _gate = new();

    private readonly List<string> _sessionRuleIds = new();
    private SessionSnapshot _current = new(SessionPhase.Guarding, null, 0, 0, AntiCheatProfile.None);
    private SessionRequest? _activeRequest;
    private string? _activeSessionId;

    /// <summary>初始化会话引擎。</summary>
    /// <param name="engine">内核规则引擎。</param>
    /// <param name="antiCheat">反作弊检测器。</param>
    /// <param name="journalStore">还原日志存储。</param>
    /// <param name="history">会话历史存储。</param>
    /// <param name="logger">日志。</param>
    /// <param name="ruleFactory">规则集工厂；null 时使用内置 <see cref="RuleSetBuilder"/>（单测可注入）。</param>
    public SessionEngine(
        TweakEngine engine,
        IAntiCheatDetector antiCheat,
        JournalStore journalStore,
        SessionHistoryStore history,
        ILogger<SessionEngine> logger,
        Func<SessionRequest, IReadOnlyList<TweakRule>>? ruleFactory = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _antiCheat = antiCheat ?? throw new ArgumentNullException(nameof(antiCheat));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ruleFactory = ruleFactory ?? RuleSetBuilder.Build;
    }

    /// <summary>会话状态变更事件。</summary>
    public event EventHandler<SessionSnapshot>? StateChanged;

    /// <summary>当前状态快照。</summary>
    public SessionSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>是否有游戏会话进行中。</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _current.Phase == SessionPhase.Playing;
            }
        }
    }

    /// <summary>
    /// 开始一次游戏会话：检测反作弊 → 按强度档构造规则 → 逐项应用（含 IFEO 降级）→ 进入"游戏中"。
    /// 任一项失败不中断整体（其余项继续），失败项在 <see cref="SessionStartResult.Outcomes"/> 中如实报告。
    /// </summary>
    public Result<SessionStartResult> BeginSession(SessionRequest request, IProgress<SessionProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (_current.Phase == SessionPhase.Playing)
            {
                return Result<SessionStartResult>.Fail($"已有游戏会话进行中（{_current.GameName}）");
            }
        }

        var detection = _antiCheat.Detect();
        var useIfeo = detection.RequiresIfeoFallback;

        var rules = _ruleFactory(request)
            .Where(rule => !request.DisabledRuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();

        SetPhase(SessionPhase.Starting, request.GameName, detection.Profile, appliedCount: 0, revertedCount: 0);
        progress?.Report(new SessionProgress("正在检查反作弊环境…", 0, rules.Count));

        var outcomes = new List<RuleOutcome>(rules.Count);
        var appliedIds = new List<string>(rules.Count);
        string? sessionId = null;
        var index = 0;
        foreach (var rule in rules)
        {
            index++;
            progress?.Report(new SessionProgress($"正在{rule.DisplayName}… {index}/{rules.Count}", index, rules.Count));

            var (outcome, ruleSessionId, appliedId) = ApplyRuleInternal(rule, useIfeo);
            outcomes.Add(outcome);
            if (ruleSessionId is not null)
            {
                sessionId ??= ruleSessionId;
            }

            // 含"已注册但本次执行失败"的规则：其还原日志条目可能已落盘（Journal 先写后做），
            // 必须一并纳入会话集合，确保 EndSession 能将其闭合（缺陷 A：杜绝残留污染幂等判定）。
            if (appliedId is not null)
            {
                appliedIds.Add(appliedId);
            }
        }

        var appliedCount = outcomes.Count(outcome => outcome.Applied || outcome.Skipped);
        lock (_gate)
        {
            _sessionRuleIds.Clear();
            _sessionRuleIds.AddRange(appliedIds);
            _activeRequest = request;
            _activeSessionId = sessionId;
        }

        SetPhase(SessionPhase.Playing, request.GameName, detection.Profile, appliedCount: appliedCount);

        var result = new SessionStartResult(
            true,
            request.GameId,
            request.GameName,
            detection.Profile,
            detection.Product,
            useIfeo,
            outcomes,
            null);

        _history.Begin(new SessionRecord(
            sessionId ?? Guid.NewGuid().ToString("N"),
            request.GameId,
            request.GameName,
            DateTime.UtcNow,
            null,
            appliedCount,
            0,
            detection.Profile.ToString(),
            FullyReverted: false));

        _logger.LogInformation(
            "会话已开始：{Game}，应用 {Applied}/{Total} 项，反作弊 {AntiCheat}，IFEO 降级 {Ifeo}",
            request.GameName,
            appliedCount,
            rules.Count,
            detection.Profile,
            useIfeo);

        return Result<SessionStartResult>.Ok(result);
    }

    /// <summary>
    /// 结束当前会话：按 LIFO 还原全部已应用规则 → 校验 → 进入"已还原"。
    /// 无进行中会话时退化为兜底还原（<see cref="RevertAll"/>）。
    /// </summary>
    public Result<SessionEndResult> EndSession(IProgress<SessionProgress>? progress = null)
    {
        List<string> ids;
        SessionRequest? request;
        string? sessionId;
        AntiCheatProfile antiCheat;
        lock (_gate)
        {
            // LIFO：后应用先还原（含"已注册但执行失败"的规则）
            ids = Enumerable.Reverse(_sessionRuleIds).ToList();
            request = _activeRequest;
            sessionId = _activeSessionId;
            antiCheat = _current.AntiCheat;
        }

        // 兜底：本产品名下任何仍处于 Open 的条目一并闭合（防跨进程残留污染幂等判定，缺陷 A）
        foreach (var ruleId in ListOpenRuleIds())
        {
            if (!ids.Contains(ruleId, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(ruleId);
            }
        }

        if (ids.Count == 0)
        {
            return RevertAll(progress);
        }

        SetPhase(SessionPhase.Reverting, request?.GameName, antiCheat);
        var revertedEntries = 0;
        var failedRuleIds = new List<string>();
        var index = 0;
        foreach (var ruleId in ids)
        {
            index++;
            progress?.Report(new SessionProgress($"正在还原设置… {index}/{ids.Count}", index, ids.Count));

            var revert = _engine.RevertRule(ruleId, ProductName);
            if (revert.IsSuccess)
            {
                revertedEntries += revert.Value!.EntriesReverted;
            }
            else
            {
                failedRuleIds.Add(ruleId);
                _logger.LogWarning("还原规则 {RuleId} 失败：{Error}", ruleId, revert.Error);
            }
        }

        var fullyReverted = failedRuleIds.Count == 0;
        lock (_gate)
        {
            _sessionRuleIds.Clear();
            _activeRequest = null;
            _activeSessionId = null;
        }

        SetPhase(SessionPhase.Restored, request?.GameName, antiCheat, appliedCount: 0, revertedCount: revertedEntries);

        if (sessionId is not null)
        {
            _history.Complete(sessionId, revertedEntries, fullyReverted);
        }

        _logger.LogInformation("会话已结束：还原 {Reverted} 项，全部还原 {FullyReverted}", revertedEntries, fullyReverted);

        return Result<SessionEndResult>.Ok(new SessionEndResult(
            fullyReverted,
            revertedEntries,
            fullyReverted,
            failedRuleIds,
            fullyReverted ? null : "部分调整项还原失败，已保留还原日志，可在下次启动自动重试"));
    }

    /// <summary>
    /// 校验当前无残留（应用过的规则均已不在"已应用"状态）。
    /// </summary>
    public Result<SessionVerification> Verify()
    {
        List<string> ids;
        lock (_gate)
        {
            ids = _sessionRuleIds.ToList();
        }

        var failures = ids.Where(id => _engine.IsApplied(id, ProductName)).ToList();
        return Result<SessionVerification>.Ok(new SessionVerification(failures.Count == 0, failures));
    }

    /// <summary>
    /// 兜底还原：扫描本产品全部未闭合会话，对其中"引擎已加载"的规则逐条还原（T18 revertAll / 托盘"全部还原"）。
    /// 引擎未加载的规则（如跨重启的 IFEO 动态规则）在本方法中如实计为失败；这类残留条目会由
    /// <see cref="RecoveryService"/> 在下次启动时凭还原日志内嵌的规则定义重建规则后自动还原
    /// （见 <see cref="TweakEngine.RestoreEntry"/>，架构 3.2 / PRD A-P0-5 "崩溃或断电后 100% 恢复"）。
    /// </summary>
    public Result<SessionEndResult> RevertAll(IProgress<SessionProgress>? progress = null)
    {
        var ruleIds = _journalStore.ListSessions()
            .Where(session => session.Status == JournalStatus.Open
                && string.Equals(session.Product, ProductName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(session => session.OpenEntries)
            .Select(entry => entry.RuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var loaded = new HashSet<string>(_engine.LoadedRules.Select(rule => rule.Id), StringComparer.OrdinalIgnoreCase);
        var revertedEntries = 0;
        var failedRuleIds = new List<string>();

        SetPhase(SessionPhase.Reverting, _current.GameName, _current.AntiCheat);
        var index = 0;
        foreach (var ruleId in ruleIds)
        {
            index++;
            progress?.Report(new SessionProgress($"正在还原设置… {index}/{ruleIds.Count}", index, ruleIds.Count));

            if (!loaded.Contains(ruleId))
            {
                failedRuleIds.Add(ruleId);
                continue;
            }

            var revert = _engine.RevertRule(ruleId, ProductName);
            if (revert.IsSuccess)
            {
                revertedEntries += revert.Value!.EntriesReverted;
            }
            else
            {
                failedRuleIds.Add(ruleId);
            }
        }

        lock (_gate)
        {
            _sessionRuleIds.Clear();
            _activeRequest = null;
            _activeSessionId = null;
        }

        SetPhase(SessionPhase.Restored, null, _current.AntiCheat, appliedCount: 0, revertedCount: revertedEntries);

        return Result<SessionEndResult>.Ok(new SessionEndResult(
            failedRuleIds.Count == 0,
            revertedEntries,
            failedRuleIds.Count == 0,
            failedRuleIds,
            null));
    }

    /// <summary>
    /// 外部按规则 Id 应用（T18 IPC <c>applyProfile</c>：产品 C"优化-复测闭环"调用）。
    /// 只接受引擎中已加载的规则；不改动"游戏中"状态（由调用方后续 revertAll 还原）。
    /// </summary>
    public Result<SessionStartResult> ApplyProfile(IReadOnlyList<string> ruleIds)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        if (ruleIds.Count == 0)
        {
            return Result<SessionStartResult>.Fail("applyProfile 需要至少一个规则 Id");
        }

        var detection = _antiCheat.Detect();
        var loaded = _engine.LoadedRules.ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<RuleOutcome>(ruleIds.Count);
        var appliedIds = new List<string>(ruleIds.Count);
        string? sessionId = null;

        foreach (var ruleId in ruleIds)
        {
            if (!loaded.TryGetValue(ruleId, out var rule))
            {
                outcomes.Add(new RuleOutcome(ruleId, ruleId, false, false, false, $"规则“{ruleId}”尚未加载"));
                continue;
            }

            var (outcome, ruleSessionId, appliedId) = ApplyRuleInternal(rule, detection.RequiresIfeoFallback);
            outcomes.Add(outcome);
            if (ruleSessionId is not null)
            {
                sessionId ??= ruleSessionId;
            }

            if (appliedId is not null)
            {
                appliedIds.Add(appliedId);
            }
        }

        lock (_gate)
        {
            _sessionRuleIds.AddRange(appliedIds);
        }

        return Result<SessionStartResult>.Ok(new SessionStartResult(
            true,
            "external",
            "外部调用",
            detection.Profile,
            detection.Product,
            detection.RequiresIfeoFallback,
            outcomes,
            null));
    }

    /// <summary>应用单条规则（必要时走 IFEO 降级），返回结果、会话 Id 与实际应用的规则 Id。</summary>
    private (RuleOutcome Outcome, string? SessionId, string? AppliedRuleId) ApplyRuleInternal(TweakRule rule, bool useIfeo)
    {
        var effectiveRule = useIfeo && IfeoFallbackPlanner.ContainsProcessSchedulingAction(rule)
            ? IfeoFallbackPlanner.BuildIfeoRule(rule)
            : rule;
        var usedIfeo = !ReferenceEquals(effectiveRule, rule);

        var register = _engine.RegisterRule(effectiveRule);
        if (!register.IsSuccess)
        {
            return (new RuleOutcome(rule.Id, rule.DisplayName, false, false, usedIfeo, register.Error), null, null);
        }

        var apply = _engine.ApplyRule(effectiveRule.Id, ProductName);
        if (!apply.IsSuccess)
        {
            // 规则已注册，且可能已写入还原日志条目（Journal 先写后做）→ 返回其 Id 供 EndSession 闭合，
            // 避免残留 Open 条目在下次会话被幂等逻辑误判为"已应用"（缺陷 A：永不重试）。
            return (new RuleOutcome(rule.Id, rule.DisplayName, false, false, usedIfeo, apply.Error), null, effectiveRule.Id);
        }

        var report = apply.Value!;
        var outcome = new RuleOutcome(rule.Id, rule.DisplayName, !report.AlreadyApplied, report.AlreadyApplied, usedIfeo, null);
        return (outcome, report.SessionId, effectiveRule.Id);
    }

    /// <summary>本产品名下仍处于 Open 的还原日志条目的规则 Id（去重；缺陷 A 兜底闭合用）。</summary>
    private List<string> ListOpenRuleIds() =>
        _journalStore.ListSessions()
            .Where(session => session.Status == JournalStatus.Open
                && string.Equals(session.Product, ProductName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(session => session.OpenEntries)
            .Select(entry => entry.RuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>更新状态快照并在锁外触发事件。</summary>
    private void SetPhase(
        SessionPhase phase,
        string? gameName = null,
        AntiCheatProfile antiCheat = AntiCheatProfile.None,
        int? appliedCount = null,
        int? revertedCount = null)
    {
        SessionSnapshot snapshot;
        lock (_gate)
        {
            _current = new SessionSnapshot(
                phase,
                gameName ?? _current.GameName,
                appliedCount ?? _current.AppliedCount,
                revertedCount ?? _current.RevertedCount,
                antiCheat);
            snapshot = _current;
        }

        StateChanged?.Invoke(this, snapshot);
    }
}
