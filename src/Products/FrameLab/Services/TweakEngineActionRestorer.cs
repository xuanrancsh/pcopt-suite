// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core;
using PcOpt.Core.Journal;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 动作还原器：把内核 <see cref="IActionRestorer"/> 接到 <see cref="PcOpt.Core.Tweak.TweakEngine"/>（架构 3.2）。
/// <see cref="RecoveryService"/> 在开机时按 LIFO 逐条调用本还原器，
/// 实现"崩溃 / 断电后系统 100% 恢复原状"（最小引擎的电源 / 计时器改动可被还原）。
/// </summary>
public sealed class TweakEngineActionRestorer : IActionRestorer
{
    private readonly PcOpt.Core.Tweak.TweakEngine _engine;

    /// <summary>初始化还原器。</summary>
    /// <param name="engine">内核规则引擎（其 <c>RestoreEntry</c> 按单条快照还原）。</param>
    public TweakEngineActionRestorer(PcOpt.Core.Tweak.TweakEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <inheritdoc/>
    public Result Restore(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _engine.RestoreEntry(entry);
    }
}
