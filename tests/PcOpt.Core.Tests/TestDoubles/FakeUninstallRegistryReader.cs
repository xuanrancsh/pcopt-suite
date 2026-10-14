// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Game;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>固定卸载注册表条目替身（T7：GOG / Xbox / WeGame 保守探测单测）。</summary>
internal sealed class FakeUninstallRegistryReader : IUninstallRegistryReader
{
    private readonly IReadOnlyList<UninstallEntry> _entries;

    /// <summary>初始化替身。</summary>
    public FakeUninstallRegistryReader(IReadOnlyList<UninstallEntry> entries)
    {
        _entries = entries;
    }

    /// <inheritdoc/>
    public IReadOnlyList<UninstallEntry> ReadEntries() => _entries;
}
