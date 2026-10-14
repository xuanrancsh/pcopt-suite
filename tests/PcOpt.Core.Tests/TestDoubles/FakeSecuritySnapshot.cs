// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Game;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>伪造系统安全态势快照（T9：无真实反作弊样本，用特征模拟单测）。</summary>
internal sealed class FakeSecuritySnapshot : ISystemSecuritySnapshot
{
    /// <summary>初始化伪造快照。</summary>
    public FakeSecuritySnapshot(
        IReadOnlyList<string>? serviceNames = null,
        IReadOnlyList<string>? driverFileNames = null,
        IReadOnlyList<string>? processNames = null)
    {
        ServiceNames = serviceNames ?? Array.Empty<string>();
        DriverFileNames = driverFileNames ?? Array.Empty<string>();
        ProcessNames = processNames ?? Array.Empty<string>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ServiceNames { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> DriverFileNames { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> ProcessNames { get; }
}
