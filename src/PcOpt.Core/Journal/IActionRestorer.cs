// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Journal;

/// <summary>
/// 动作还原器（T2 抽象，真实实现由 TweakEngine / SessionEngine 在 T13 接入）。
/// RecoveryService 按 LIFO 逐项调用本接口完成开机恢复；本轮单元测试使用 fake 实现。
/// </summary>
public interface IActionRestorer
{
    /// <summary>还原单条日志条目对应的动作。</summary>
    /// <param name="entry">待还原条目（含原值快照 JSON）。</param>
    /// <returns>还原结果；失败时 Error 必须明确（不含用户数据）。</returns>
    Result Restore(JournalEntry entry);
}
