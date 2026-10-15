// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json.Serialization;

namespace PcOpt.Core.Journal;

/// <summary>
/// 还原日志条目：一次动作应用的原始值快照（架构 3.2）。
/// 条目创建后不可变（init-only），还原时通过 <see cref="WithReverted"/> 生成已标记的新条目，
/// 保证"先写 Journal 后执行"的只增日志模型。
/// </summary>
public sealed class JournalEntry
{
    /// <summary>初始化条目。</summary>
    /// <param name="ruleId">规则 Id（分类.对象.动作）。</param>
    /// <param name="actionIndex">动作在规则 <c>Actions</c> 中的序号（还原时按 LIFO 倒序）。</param>
    /// <param name="originalValueJson">原值快照 JSON（含"键 / 值不存在"语义）。</param>
    /// <param name="appliedAt">应用时间（UTC）。</param>
    /// <param name="revertedAt">还原时间（UTC）；未还原为 null。</param>
    /// <param name="ruleDefinitionYaml">
    /// 规则定义 YAML 快照（可选）。用于崩溃 / 重启恢复时重建"仅会话期动态注册"的规则
    /// （如命中内核级反作弊时 IFEO 降级产生的 <c>*.ifeo</c> 规则）——
    /// 收录于还原日志，保证 HKLM 等持久写入在下次启动也能被自动还原（架构 3.2 / PRD A-P0-5）。
    /// </param>
    [JsonConstructor]
    public JournalEntry(
        string ruleId,
        int actionIndex,
        string originalValueJson,
        DateTime appliedAt,
        DateTime? revertedAt = null,
        string? ruleDefinitionYaml = null)
    {
        RuleId = ruleId;
        ActionIndex = actionIndex;
        OriginalValueJson = originalValueJson;
        AppliedAt = appliedAt;
        RevertedAt = revertedAt;
        RuleDefinitionYaml = ruleDefinitionYaml;
    }

    /// <summary>规则 Id。</summary>
    public string RuleId { get; init; }

    /// <summary>动作序号。</summary>
    public int ActionIndex { get; init; }

    /// <summary>原值快照 JSON。</summary>
    public string OriginalValueJson { get; init; }

    /// <summary>应用时间（UTC；UI 展示时再转换为本地时间）。</summary>
    public DateTime AppliedAt { get; init; }

    /// <summary>还原时间（UTC）；未还原为 null。</summary>
    public DateTime? RevertedAt { get; init; }

    /// <summary>规则定义 YAML 快照（重建动态规则用）；无则为 null。</summary>
    public string? RuleDefinitionYaml { get; init; }

    /// <summary>是否尚未还原。</summary>
    public bool IsOpen => RevertedAt is null;

    /// <summary>返回标记了还原时间的新条目（保留规则定义快照）。</summary>
    public JournalEntry WithReverted(DateTime revertedAt) =>
        new(RuleId, ActionIndex, OriginalValueJson, AppliedAt, revertedAt, RuleDefinitionYaml);
}
