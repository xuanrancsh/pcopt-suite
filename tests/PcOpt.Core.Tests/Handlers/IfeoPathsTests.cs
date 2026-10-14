// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Tweak.Handlers;

namespace PcOpt.Core.Tests.Handlers;

/// <summary>
/// <see cref="IfeoPaths"/> 单元测试（PRD A-P0-6：IFEO 降级方案复用注册表 handler）。
/// </summary>
public sealed class IfeoPathsTests
{
    [Fact]
    public void PerfOptions_ShouldReturnExpectedPath()
    {
        var path = IfeoPaths.PerfOptions("cs2.exe");

        path.Should().Be(
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe\PerfOptions");
    }

    [Fact]
    public void PerfOptions_ShouldStripDirectoryFromFullPath()
    {
        var path = IfeoPaths.PerfOptions(@"C:\Games\Counter-Strike Global Offensive\cs2.exe");

        path.Should().EndWith(@"\cs2.exe\PerfOptions");
    }

    [Fact]
    public void PerfOptions_BlankName_ShouldThrow()
    {
        var act = () => IfeoPaths.PerfOptions("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CpuPriorityClassValues_ShouldFollowWin32Encoding()
    {
        IfeoPaths.CpuPriorityClassValues.Normal.Should().Be(3);
        IfeoPaths.CpuPriorityClassValues.High.Should().Be(5);
        IfeoPaths.CpuPriorityClassValues.Idle.Should().Be(1);
    }
}
