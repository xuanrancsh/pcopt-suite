// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>已应用规则信息（还原中心列表）。</summary>
/// <param name="RuleId">规则 Id。</param>
/// <param name="DisplayName">人话名。</param>
/// <param name="Category">分类。</param>
/// <param name="Risk">风险等级。</param>
/// <param name="AppliedAtUtc">最近一次应用时间（UTC）。</param>
public sealed record AppliedRuleInfo(string RuleId, string DisplayName, string Category, RiskLevel Risk, DateTime AppliedAtUtc);

/// <summary>还原日志会话信息（还原中心列表）。</summary>
/// <param name="SessionId">会话 Id。</param>
/// <param name="Product">产品名。</param>
/// <param name="StartedAtUtc">开始时间（UTC）。</param>
/// <param name="Status">会话状态。</param>
/// <param name="OpenEntries">未还原条目数。</param>
/// <param name="TotalEntries">条目总数。</param>
public sealed record JournalSessionInfo(string SessionId, string Product, DateTime StartedAtUtc, string Status, int OpenEntries, int TotalEntries);

/// <summary>
/// 还原中心服务（T29，PRD B-P0-9）：单条还原 / 全部还原 / 创建系统还原点 / 操作日志导出。
/// 与内核 Journal 同一实现（复用 <see cref="TweakEngine"/> 与 <see cref="JournalStore"/>），
/// 还原后校验通过（引擎内部保证）。
/// </summary>
public sealed class RestoreCenterService
{
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TweakEngine _engine;
    private readonly RuleRepository _rules;
    private readonly JournalStore _journalStore;
    private readonly IRestorePointService _restorePoint;
    private readonly ILogger<RestoreCenterService> _logger;

    /// <summary>初始化还原中心服务。</summary>
    public RestoreCenterService(
        TweakEngine engine,
        RuleRepository rules,
        JournalStore journalStore,
        IRestorePointService restorePoint,
        ILogger<RestoreCenterService> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _restorePoint = restorePoint ?? throw new ArgumentNullException(nameof(restorePoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>列出当前已应用的规则（含最近应用时间）。</summary>
    public IReadOnlyList<AppliedRuleInfo> ListAppliedRules()
    {
        var openedAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in _journalStore.ListSessions().Where(s => s.Status == JournalStatus.Open))
        {
            foreach (var entry in session.OpenEntries)
            {
                if (!openedAt.TryGetValue(entry.RuleId, out var existing) || entry.AppliedAt > existing)
                {
                    openedAt[entry.RuleId] = entry.AppliedAt;
                }
            }
        }

        var result = new List<AppliedRuleInfo>();
        foreach (var rule in _engine.LoadedRules)
        {
            if (!_engine.IsApplied(rule.Id, RuleRepository.ProductName))
            {
                continue;
            }

            result.Add(new AppliedRuleInfo(
                rule.Id,
                rule.DisplayName,
                rule.Category,
                rule.Risk,
                openedAt.TryGetValue(rule.Id, out var at) ? at : DateTime.UtcNow));
        }

        return result.OrderByDescending(info => info.AppliedAtUtc).ToList();
    }

    /// <summary>列出全部还原日志会话（最近在前）。</summary>
    public IReadOnlyList<JournalSessionInfo> ListSessions() =>
        _journalStore.ListSessions()
            .OrderByDescending(session => session.StartedAt)
            .Select(session => new JournalSessionInfo(
                session.SessionId,
                session.Product,
                session.StartedAt,
                session.Status.ToString(),
                session.OpenEntries.Count(),
                session.Entries.Count))
            .ToList();

    /// <summary>还原单条规则（失败返回人话原因；成功返回本次还原统计）。</summary>
    public Result<RuleRevertReport> RevertRule(string ruleId) => _rules.Revert(ruleId);

    /// <summary>还原全部已应用规则。</summary>
    public Result<RevertAllReport> RevertAll() => _rules.RevertAll();

    /// <summary>创建系统还原点。</summary>
    public Result<string> CreateRestorePoint(string description) => _restorePoint.Create(description);

    /// <summary>
    /// 导出操作日志（会话 + 当前已应用规则）为 JSON 文件。
    /// 不包含任何用户数据、硬件序列号或含用户名的路径（架构 8.3）。
    /// </summary>
    /// <param name="filePath">导出目标路径。</param>
    public Result<string> ExportLog(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<string>.Fail("导出路径为空");
        }

        try
        {
            var payload = new OperationLog(
                DateTime.UtcNow,
                ListSessions(),
                ListAppliedRules().Select(info => new AppliedRuleLog(
                    info.RuleId,
                    info.DisplayName,
                    info.Category,
                    info.Risk.ToString(),
                    info.AppliedAtUtc)).ToList());

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, JsonSerializer.Serialize(payload, ExportOptions));
            _logger.LogInformation("已导出操作日志（{Sessions} 个会话）", payload.Sessions.Count);
            return Result<string>.Ok($"已导出操作日志到 {Path.GetFileName(filePath)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"导出操作日志失败：{ex.Message}");
        }
    }

    private sealed record OperationLog(
        DateTime ExportedAtUtc,
        IReadOnlyList<JournalSessionInfo> Sessions,
        IReadOnlyList<AppliedRuleLog> AppliedRules);

    private sealed record AppliedRuleLog(
        string RuleId,
        string DisplayName,
        string Category,
        string Risk,
        DateTime AppliedAtUtc);
}
