// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json.Serialization;

namespace PcOpt.Core.Journal;

/// <summary>
/// 还原日志会话：一次"产品应用优化"过程的完整记录（架构 3.2）。
/// 存储于 <c>%ProgramData%\pcopt\journal\{SessionId}.json</c>，原子写入防半页文件。
/// </summary>
public sealed class Journal
{
    /// <summary>初始化会话。</summary>
    /// <param name="sessionId">会话 Id（Guid，去掉分隔符）。</param>
    /// <param name="product">产品名（GameModePilot / GameOptBox / FrameLab）。</param>
    /// <param name="startedAt">开始时间（UTC）。</param>
    /// <param name="status">会话状态。</param>
    /// <param name="entries">条目列表。</param>
    [JsonConstructor]
    public Journal(string sessionId, string product, DateTime startedAt, JournalStatus status, List<JournalEntry>? entries = null)
    {
        SessionId = sessionId;
        Product = product;
        StartedAt = startedAt;
        Status = status;
        Entries = entries ?? new List<JournalEntry>();
    }

    /// <summary>会话 Id。</summary>
    public string SessionId { get; init; }

    /// <summary>产品名。</summary>
    public string Product { get; init; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>会话状态。</summary>
    public JournalStatus Status { get; init; }

    /// <summary>全部条目（按应用顺序）。</summary>
    public List<JournalEntry> Entries { get; init; }

    /// <summary>尚未还原的条目。</summary>
    public IEnumerable<JournalEntry> OpenEntries => Entries.Where(e => e.IsOpen);

    /// <summary>返回改变状态后的新会话（条目列表为副本，避免调用方意外共享可变列表）。</summary>
    public Journal WithStatus(JournalStatus status) =>
        new(SessionId, Product, StartedAt, status, Entries.ToList());
}
