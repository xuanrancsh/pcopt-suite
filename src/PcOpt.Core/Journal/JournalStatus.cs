// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Journal;

/// <summary>
/// 还原日志会话状态（架构 3.2）。
/// </summary>
public enum JournalStatus
{
    /// <summary>会话进行中（未正常关闭；进程崩溃 / 断电后仍为此状态）。</summary>
    Open,

    /// <summary>会话正常结束（全部还原完成后关闭）。</summary>
    Closed,

    /// <summary>开机恢复扫描后关闭（RecoveryService 标记）。</summary>
    Recovered,
}
