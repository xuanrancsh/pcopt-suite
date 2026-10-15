// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Config;
using PcOpt.Core.Journal;
using PcOpt.Core.Platform;
// 别名消歧：Journal 在本命名空间作用域内既指 PcOpt.Core.Journal 命名空间又指同名模型类型
using RestoreJournal = PcOpt.Core.Journal.Journal;

namespace PcOpt.Core.Tweak;

/// <summary>规则应用报告。</summary>
/// <param name="WasApplied">本次是否真实执行了动作。</param>
/// <param name="AlreadyApplied">是否因已应用（幂等命中）而跳过。</param>
/// <param name="ActionsApplied">执行的动作数量。</param>
/// <param name="SessionId">还原日志会话 Id（已应用时非空；可从 Journal 审计 / 还原）。</param>
public sealed record RuleApplyReport(bool WasApplied, bool AlreadyApplied, int ActionsApplied, string? SessionId);

/// <summary>规则还原报告。</summary>
/// <param name="EntriesReverted">还原的条目数。</param>
/// <param name="FullyReverted">是否全部还原成功。</param>
public sealed record RuleRevertReport(int EntriesReverted, bool FullyReverted);

/// <summary>
/// tweak 引擎（架构 3.1）。
/// 流程：<c>Load</c>（YAML + Ed25519 验签，失败拒绝）→ <c>Match</c>（条件过滤）→
/// <c>Snapshot</c>（先写 Journal 再动手）→ <c>Apply</c>（幂等）→ <c>Verify</c>（可选）。
/// 还原：<c>RevertRule</c> 按 Journal LIFO 逐项恢复原值。
/// 铁律（架构 8.4）：任何 Apply 之前 Journal 必须已落盘；失败时保证系统可恢复是最高优先级。
/// </summary>
public sealed class TweakEngine
{
    private readonly Dictionary<Type, ITweakActionHandler> _handlers = new();
    private readonly Dictionary<Type, ITweakActionHandler> _handlerCache = new();
    private readonly Dictionary<string, TweakRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly JournalStore _journalStore;
    private readonly ISystemEnvironment _environment;
    private readonly RuleSignatureVerifier? _signatureVerifier;
    private readonly ILogger<TweakEngine> _logger;

    /// <summary>初始化引擎。</summary>
    /// <param name="handlers">动作处理器集合（开闭原则扩展点；同类型重复注册视为配置错误）。</param>
    /// <param name="journalStore">还原日志存储。</param>
    /// <param name="environment">系统环境抽象（测试可注入替身）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="signatureVerifier">规则包签名验证器；为 null 时拒绝加载签名规则包（<see cref="LoadRulePackage"/>）。</param>
    public TweakEngine(
        IEnumerable<ITweakActionHandler> handlers,
        JournalStore journalStore,
        ISystemEnvironment environment,
        ILogger<TweakEngine> logger,
        RuleSignatureVerifier? signatureVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signatureVerifier = signatureVerifier;

        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (!_handlers.TryAdd(handler.GetType(), handler))
            {
                throw new ArgumentException($"检测到重复注册的动作处理器：{handler.GetType().Name}", nameof(handlers));
            }
        }
    }

    /// <summary>已加载的规则（按 Id）。</summary>
    public IReadOnlyCollection<TweakRule> LoadedRules => _rules.Values.ToList();

    /// <summary>
    /// 直接注册内存中构造的规则（测试 / 程序内规则；跳过 YAML 解析）。
    /// </summary>
    public Result<TweakRule> RegisterRule(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            return Result<TweakRule>.Fail("规则 Id 不能为空");
        }

        if (rule.Actions is null || rule.Actions.Length == 0)
        {
            return Result<TweakRule>.Fail($"规则“{rule.Id}”必须包含至少一个动作");
        }

        _rules[rule.Id] = rule;
        return Result<TweakRule>.Ok(rule);
    }

    /// <summary>
    /// 加载未签名 YAML 规则（仅解析，不验签）。
    /// 适用于：单元测试、规则预览、用户本地自定义规则（UI 需明示"非官方签名，风险自担"）。
    /// 官方规则包请使用 <see cref="LoadRulePackage"/>。
    /// </summary>
    public Result<TweakRule> LoadRuleFromYaml(string yaml)
    {
        var parsed = TweakRuleParser.Parse(yaml);
        if (!parsed.IsSuccess)
        {
            return Result<TweakRule>.Fail(parsed.Error);
        }

        return RegisterRule(parsed.Value!);
    }

    /// <summary>
    /// 加载签名规则包：先验签（未签名 / 签名不符 → 拒绝执行），通过后执行
    /// <see cref="OfficialRulePolicy"/> 官方包内容策略（合规红线 #9：不得含不可自动还原的动作），
    /// 最后解析注册（架构 3.5）。
    /// </summary>
    /// <param name="content">YAML 原始字节。</param>
    /// <param name="signature">.sig 分离签名字节；为 null 或空视为未签名，拒绝加载。</param>
    public Result<TweakRule> LoadRulePackage(byte[] content, byte[]? signature)
    {
        if (_signatureVerifier is null)
        {
            return Result<TweakRule>.Fail("引擎未配置签名公钥，拒绝加载签名规则包");
        }

        var verify = _signatureVerifier.Verify(content, signature);
        if (!verify.IsSuccess)
        {
            _logger.LogWarning("规则包验签失败：{Error}", verify.Error);
            return Result<TweakRule>.Fail(verify.Error);
        }

        // 合规红线 #9：官方签名包只含可自动还原的动作（Shell / powercfg 删除计划等一律拒绝）
        var parsed = TweakRuleParser.Parse(Encoding.UTF8.GetString(content));
        if (!parsed.IsSuccess)
        {
            return Result<TweakRule>.Fail(parsed.Error);
        }

        var policyCheck = OfficialRulePolicy.ValidateRule(parsed.Value!);
        if (!policyCheck.IsSuccess)
        {
            _logger.LogWarning("官方规则包被红线 #9 策略拒绝：{Error}", policyCheck.Error);
            return Result<TweakRule>.Fail(policyCheck.Error);
        }

        return RegisterRule(parsed.Value!);
    }

    /// <summary>
    /// 应用规则（幂等）。顺序：已加载检查 → 条件匹配 → 幂等检查 →
    /// 全部动作快照预检 → 先写 Journal（原子落盘）→ 逐项执行。
    /// 任一步骤失败都不会触碰系统（快照阶段失败）或保留可还原的 Journal（执行阶段失败）。
    /// </summary>
    /// <param name="ruleId">规则 Id。</param>
    /// <param name="product">产品名（GameModePilot / GameOptBox / FrameLab），用于 Journal 归属。</param>
    public Result<RuleApplyReport> ApplyRule(string ruleId, string product)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return Result<RuleApplyReport>.Fail("规则 Id 不能为空");
        }

        if (string.IsNullOrWhiteSpace(product))
        {
            return Result<RuleApplyReport>.Fail("产品名不能为空");
        }

        if (!_rules.TryGetValue(ruleId, out var rule))
        {
            return Result<RuleApplyReport>.Fail($"规则“{ruleId}”尚未加载到引擎");
        }

        if (!OsConditionMatcher.Matches(rule.Conditions, _environment))
        {
            _logger.LogInformation("规则 {RuleId} 不适用于当前系统环境，已跳过", ruleId);
            return Result<RuleApplyReport>.Fail($"规则“{ruleId}”不适用于当前系统环境（OS 版本 / CPU 架构 / GPU 厂商条件不满足）");
        }

        // 幂等：Journal 中已有该产品、该规则的未还原条目 → 视为已应用（跨进程重启同样有效）。
        // 关键路径：读还原日志失败必须显式失败，绝不把"读不到"当成"未应用"而重复执行。
        var existingResult = TryFindOpenEntries(ruleId, product);
        if (!existingResult.IsSuccess)
        {
            return Result<RuleApplyReport>.Fail($"无法读取还原日志，已终止应用以避免重复执行：{existingResult.Error}");
        }

        var existing = existingResult.Value!;
        if (existing.Count > 0)
        {
            _logger.LogInformation("规则 {RuleId} 已应用，跳过重复执行（幂等）", ruleId);
            return Result<RuleApplyReport>.Ok(new RuleApplyReport(false, true, existing.Count, existing[0].SessionId));
        }

        // 1) 预检：全部动作快照成功后才允许进入执行阶段（此阶段失败不触碰系统、不写 Journal）
        var snapshots = new List<string>(rule.Actions.Length);
        for (var i = 0; i < rule.Actions.Length; i++)
        {
            var action = rule.Actions[i];
            var handler = ResolveHandler(action);
            if (handler is null)
            {
                return Result<RuleApplyReport>.Fail($"规则“{ruleId}”动作 {i} 没有注册的处理器（{action.GetType().Name}）");
            }

            var snapshot = handler.CaptureOriginal(action);
            if (!snapshot.IsSuccess)
            {
                return Result<RuleApplyReport>.Fail($"规则“{ruleId}”动作 {i} 原值快照失败：{snapshot.Error}");
            }

            snapshots.Add(snapshot.Value ?? "{}");
        }

        // 2) 先写 Journal 后执行（架构铁律）：开启会话并原子落盘全部条目
        var sessionResult = _journalStore.BeginSession(product);
        if (!sessionResult.IsSuccess)
        {
            return Result<RuleApplyReport>.Fail($"无法开启还原日志会话：{sessionResult.Error}");
        }

        var sessionId = sessionResult.Value!;
        var appliedAt = DateTime.UtcNow;
        // 规则定义快照：供崩溃 / 重启恢复重建"仅会话期动态注册"的规则（如 IFEO 降级规则）。
        // 尽力而为：测试替身等无法序列化的动作只跳过快照，绝不中断应用流程。
        var ruleDefinitionYaml = TweakRuleWriter.TryWrite(rule, out var yaml) ? yaml : null;
        var entries = new List<JournalEntry>(rule.Actions.Length);
        for (var i = 0; i < rule.Actions.Length; i++)
        {
            entries.Add(new JournalEntry(ruleId, i, snapshots[i], appliedAt, revertedAt: null, ruleDefinitionYaml: ruleDefinitionYaml));
        }

        var append = _journalStore.AppendEntries(sessionId, entries);
        if (!append.IsSuccess)
        {
            return Result<RuleApplyReport>.Fail($"还原日志写入失败，已终止执行（保证系统可恢复）：{append.Error}");
        }

        // 3) 逐项执行（各 handler 自身保证幂等）
        for (var i = 0; i < rule.Actions.Length; i++)
        {
            var handler = ResolveHandler(rule.Actions[i])!;
            var result = handler.Apply(rule.Actions[i]);
            if (!result.IsSuccess)
            {
                return Result<RuleApplyReport>.Fail(
                    $"规则“{ruleId}”动作 {i} 执行失败：{result.Error}（原始值已记录于还原日志，可安全还原）");
            }
        }

        _logger.LogInformation("规则 {RuleId} 已应用（{Count} 个动作，SessionId: {SessionId}）", ruleId, rule.Actions.Length, sessionId);
        return Result<RuleApplyReport>.Ok(new RuleApplyReport(true, false, rule.Actions.Length, sessionId));
    }

    /// <summary>
    /// 还原规则：按 Journal LIFO 逐项恢复原值；全部成功后关闭会话（<see cref="JournalStatus.Closed"/>）。
    /// 任一项失败即中止并返回明确错误（会话保持 Open，可重试 / 开机恢复兜底）。
    /// </summary>
    public Result<RuleRevertReport> RevertRule(string ruleId, string product)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return Result<RuleRevertReport>.Fail("规则 Id 不能为空");
        }

        if (string.IsNullOrWhiteSpace(product))
        {
            return Result<RuleRevertReport>.Fail("产品名不能为空");
        }

        if (!_rules.TryGetValue(ruleId, out var rule))
        {
            return Result<RuleRevertReport>.Fail($"规则“{ruleId}”尚未加载到引擎");
        }

        // 关键路径：读还原日志失败必须显式失败——绝不静默返回"已全部还原（0 条）"，
        // 那会让用户在什么都没还原的情况下看到"已全部还原"（缺陷 E）。
        var openResult = TryFindOpenEntries(ruleId, product);
        if (!openResult.IsSuccess)
        {
            return Result<RuleRevertReport>.Fail($"无法读取还原日志，已终止还原（避免误报“已还原”）：{openResult.Error}");
        }

        var openEntries = openResult.Value!;
        if (openEntries.Count == 0)
        {
            return Result<RuleRevertReport>.Ok(new RuleRevertReport(0, true));
        }

        var reverted = 0;
        foreach (var sessionGroup in openEntries.GroupBy(e => e.SessionId))
        {
            var load = _journalStore.Load(sessionGroup.Key);
            if (!load.IsSuccess)
            {
                return Result<RuleRevertReport>.Fail(load.Error);
            }

            var session = load.Value;
            if (session is null)
            {
                return Result<RuleRevertReport>.Fail($"还原日志会话不存在（SessionId: {sessionGroup.Key}）");
            }

            var updatedEntries = new List<JournalEntry>(session.Entries);

            // LIFO：同一会话内动作序号大的先还原
            foreach (var entry in sessionGroup.OrderByDescending(e => e.Entry.ActionIndex))
            {
                var journalEntry = entry.Entry;
                if (journalEntry.ActionIndex < 0 || journalEntry.ActionIndex >= rule.Actions.Length)
                {
                    return Result<RuleRevertReport>.Fail($"还原日志动作序号越界：规则“{ruleId}”动作 {journalEntry.ActionIndex}");
                }

                var action = rule.Actions[journalEntry.ActionIndex];
                var handler = ResolveHandler(action);
                if (handler is null)
                {
                    return Result<RuleRevertReport>.Fail($"规则“{ruleId}”动作 {journalEntry.ActionIndex} 没有注册的处理器");
                }

                var result = handler.Revert(action, journalEntry.OriginalValueJson);
                if (!result.IsSuccess)
                {
                    return Result<RuleRevertReport>.Fail($"规则“{ruleId}”动作 {journalEntry.ActionIndex} 还原失败：{result.Error}");
                }

                var index = updatedEntries.FindIndex(e =>
                    string.Equals(e.RuleId, journalEntry.RuleId, StringComparison.OrdinalIgnoreCase)
                    && e.ActionIndex == journalEntry.ActionIndex
                    && e.IsOpen);
                if (index >= 0)
                {
                    updatedEntries[index] = journalEntry.WithReverted(DateTime.UtcNow);
                }

                reverted++;
            }

            var hasOtherOpenEntries = updatedEntries.Any(e => e.IsOpen);
            var finalSession = new RestoreJournal(
                session.SessionId,
                session.Product,
                session.StartedAt,
                hasOtherOpenEntries ? JournalStatus.Open : JournalStatus.Closed,
                updatedEntries);
            var save = _journalStore.Save(finalSession);
            if (!save.IsSuccess)
            {
                return Result<RuleRevertReport>.Fail($"还原日志更新失败：{save.Error}");
            }

            _logger.LogInformation("规则 {RuleId} 已还原（SessionId: {SessionId}）", ruleId, sessionGroup.Key);
        }

        return Result<RuleRevertReport>.Ok(new RuleRevertReport(reverted, true));
    }

    /// <summary>
    /// 查询规则当前是否已应用（以 Journal 未还原条目为准，跨进程重启有效）。
    /// <para>展示 / 徽标等<b>非关键</b>查询：读还原日志失败时按"未应用"降级（宽松扫描）。
    /// 还原判定请走 <see cref="RevertRule"/> / <see cref="ListOpenRuleIds"/>（严格读取）。</para>
    /// </summary>
    public bool IsApplied(string ruleId, string product) =>
        QueryOpenEntries(_journalStore.ListSessions(), ruleId, product).Count > 0;

    /// <summary>
    /// 严格列出指定产品名下所有"未还原条目"所属的规则 Id（去重）。
    /// 用于"全部还原"等批量关键路径：读还原日志失败时显式上抛，
    /// 绝不因读不到而漏还原并误报成功。
    /// </summary>
    /// <param name="product">产品名。</param>
    public Result<IReadOnlyList<string>> ListOpenRuleIds(string product)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            return Result<IReadOnlyList<string>>.Fail("产品名不能为空");
        }

        var sessions = _journalStore.TryListSessions();
        if (!sessions.IsSuccess)
        {
            return Result<IReadOnlyList<string>>.Fail(sessions.Error);
        }

        var ids = sessions.Value!
            .Where(s => s.Status == JournalStatus.Open
                && string.Equals(s.Product, product, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.OpenEntries)
            .Select(e => e.RuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Result<IReadOnlyList<string>>.Ok(ids);
    }

    /// <summary>
    /// 按单条还原日志条目还原动作（T13 接入 <c>IActionRestorer</c>：开机恢复服务负责 LIFO 顺序，
    /// 本方法只负责单条还原）。
    /// 前提：该条目所属规则必须已加载（<see cref="RegisterRule"/> / YAML 解析）。
    /// 失败原因明确（中文），不含用户数据（架构 8.2 / 8.3）。
    /// </summary>
    /// <param name="entry">待还原的还原日志条目（含原值快照 JSON）。</param>
    public Result RestoreEntry(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!_rules.TryGetValue(entry.RuleId, out var rule))
        {
            // 崩溃 / 重启恢复：仅会话期动态注册的规则（如 IFEO 降级）不在内存中，
            // 尝试由条目内嵌的规则定义重建；重建失败才判定为"尚未加载"。
            var rebuilt = TryRebuildRuleFromDefinition(entry);
            if (rebuilt is null)
            {
                return Result.Fail($"规则“{entry.RuleId}”尚未加载，无法还原该条目");
            }

            rule = rebuilt;
        }

        if (entry.ActionIndex < 0 || entry.ActionIndex >= rule.Actions.Length)
        {
            return Result.Fail($"还原日志动作序号越界：规则“{entry.RuleId}”动作 {entry.ActionIndex}");
        }

        var action = rule.Actions[entry.ActionIndex];
        var handler = ResolveHandler(action);
        if (handler is null)
        {
            return Result.Fail($"规则“{entry.RuleId}”动作 {entry.ActionIndex} 没有注册的处理器");
        }

        return handler.Revert(action, entry.OriginalValueJson);
    }

    /// <summary>
    /// 由还原日志条目内嵌的规则定义重建规则（崩溃 / 重启恢复动态规则用，架构 3.2 / PRD A-P0-5）。
    /// 仅当条目携带 <see cref="JournalEntry.RuleDefinitionYaml"/>、解析成功且解析出的 Id
    /// 与条目 RuleId 一致时成功；否则返回 null（不得凭异源定义误还原）。
    /// </summary>
    private TweakRule? TryRebuildRuleFromDefinition(JournalEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.RuleDefinitionYaml))
        {
            return null;
        }

        var parsed = TweakRuleParser.Parse(entry.RuleDefinitionYaml);
        if (!parsed.IsSuccess || parsed.Value is null)
        {
            _logger.LogWarning("还原日志内嵌的规则定义无法解析（规则: {RuleId}）：{Error}", entry.RuleId, parsed.Error);
            return null;
        }

        if (!string.Equals(parsed.Value.Id, entry.RuleId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "还原日志内嵌规则定义 Id 不一致（条目: {RuleId}，定义: {DefinedId}），已拒绝重建",
                entry.RuleId,
                parsed.Value.Id);
            return null;
        }

        _rules[parsed.Value.Id] = parsed.Value;
        _logger.LogInformation("已由还原日志重建动态规则：{RuleId}", entry.RuleId);
        return parsed.Value;
    }

    /// <summary>
    /// 严格查询"该产品、该规则"的未还原条目：还原 / 幂等判定等关键路径使用。
    /// 读还原日志失败（I/O）时返回失败，绝不把"读不到"当作"无待还原项"（内核可还原承诺）。
    /// </summary>
    private Result<List<(JournalEntry Entry, string SessionId)>> TryFindOpenEntries(string ruleId, string product)
    {
        var sessions = _journalStore.TryListSessions();
        if (!sessions.IsSuccess)
        {
            return Result<List<(JournalEntry Entry, string SessionId)>>.Fail(sessions.Error);
        }

        return Result<List<(JournalEntry Entry, string SessionId)>>.Ok(QueryOpenEntries(sessions.Value!, ruleId, product));
    }

    /// <summary>从给定会话集合筛选"该产品、该规则"的未还原条目（纯函数，供宽松 / 严格路径共用）。</summary>
    private static List<(JournalEntry Entry, string SessionId)> QueryOpenEntries(
        IReadOnlyList<RestoreJournal> sessions,
        string ruleId,
        string product) =>
        sessions
            .Where(s => s.Status == JournalStatus.Open
                && string.Equals(s.Product, product, StringComparison.OrdinalIgnoreCase))
            .SelectMany(s => s.OpenEntries.Select(e => (Entry: e, SessionId: s.SessionId)))
            .Where(x => string.Equals(x.Entry.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.SessionId, StringComparer.Ordinal)
            .ToList();

    private ITweakActionHandler? ResolveHandler(TweakAction action)
    {
        var actionType = action.GetType();
        if (_handlerCache.TryGetValue(actionType, out var cached))
        {
            return cached;
        }

        foreach (var handler in _handlers.Values)
        {
            if (handler.CanHandle(action))
            {
                _handlerCache[actionType] = handler;
                return handler;
            }
        }

        return null;
    }
}
