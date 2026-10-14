// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Journal;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>测试用动作还原器：记录还原调用，行为可通过委托注入。</summary>
internal sealed class FakeActionRestorer : IActionRestorer
{
    /// <summary>被要求还原的条目（按调用顺序）。</summary>
    public List<JournalEntry> RestoredEntries { get; } = new();

    /// <summary>还原行为注入；默认成功。</summary>
    public Func<JournalEntry, Result>? RestoreHandler { get; set; }

    public Result Restore(JournalEntry entry)
    {
        RestoredEntries.Add(entry);
        return RestoreHandler?.Invoke(entry) ?? Result.Ok();
    }
}
