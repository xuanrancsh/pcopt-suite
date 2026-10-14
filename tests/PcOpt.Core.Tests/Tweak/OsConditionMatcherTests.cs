// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Platform;
using PcOpt.Core.Tests.TestDoubles;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.Tweak;

/// <summary>
/// <see cref="OsConditionMatcher"/> 单元测试：OS Build 范围 / CPU 架构 / GPU 厂商（含未知保守策略）。
/// </summary>
public sealed class OsConditionMatcherTests
{
    private readonly ISystemEnvironment _environment = TestSystemEnvironment.Win11X64();

    [Fact]
    public void Matches_EmptyConditions_ShouldReturnTrue()
    {
        OsConditionMatcher.Matches(Array.Empty<OsCondition>(), _environment).Should().BeTrue();
        OsConditionMatcher.Matches(null, _environment).Should().BeTrue();
    }

    [Fact]
    public void Matches_BuildWithinRange_ShouldReturnTrue()
    {
        var conditions = new[] { new OsCondition { MinBuild = 19045, MaxBuild = 26100 } };

        OsConditionMatcher.Matches(conditions, _environment).Should().BeTrue();
    }

    [Fact]
    public void Matches_BuildBelowMinimum_ShouldReturnFalse()
    {
        var conditions = new[] { new OsCondition { MinBuild = 22632 } };

        OsConditionMatcher.Matches(conditions, _environment).Should().BeFalse();
    }

    [Fact]
    public void Matches_BuildAboveMaximum_ShouldReturnFalse()
    {
        var conditions = new[] { new OsCondition { MaxBuild = 20000 } };

        OsConditionMatcher.Matches(conditions, _environment).Should().BeFalse();
    }

    [Fact]
    public void Matches_CpuArchitecture_ShouldBeCaseInsensitive()
    {
        OsConditionMatcher.Matches(new[] { new OsCondition { CpuArchitecture = "x64" } }, _environment).Should().BeTrue();
        OsConditionMatcher.Matches(new[] { new OsCondition { CpuArchitecture = "X64" } }, _environment).Should().BeTrue();
        OsConditionMatcher.Matches(new[] { new OsCondition { CpuArchitecture = "Arm64" } }, _environment).Should().BeFalse();
    }

    [Fact]
    public void Matches_UnrecognizedArchitecture_ShouldReturnFalse()
    {
        OsConditionMatcher.Matches(new[] { new OsCondition { CpuArchitecture = "PowerPC" } }, _environment).Should().BeFalse();
    }

    [Fact]
    public void Matches_GpuVendorUnknownEnvironment_ShouldBeConservative()
    {
        // GPU 检测未接入（T6 前）时，指定厂商的规则不应用（宁可不用，不可误用）
        OsConditionMatcher.Matches(new[] { new OsCondition { GpuVendor = "NVIDIA" } }, _environment).Should().BeFalse();
    }

    [Fact]
    public void Matches_GpuVendorWildcard_ShouldReturnTrue()
    {
        OsConditionMatcher.Matches(new[] { new OsCondition { GpuVendor = "any" } }, _environment).Should().BeTrue();
        OsConditionMatcher.Matches(new[] { new OsCondition { GpuVendor = "unknown" } }, _environment).Should().BeTrue();
        OsConditionMatcher.Matches(new[] { new OsCondition() }, _environment).Should().BeTrue();
    }

    [Fact]
    public void Matches_GpuVendorMatch_ShouldBeCaseInsensitive()
    {
        var nvidiaEnv = new TestSystemEnvironment(
            new OsVersionInfo("Windows", new Version(10, 0, 22631), 22631),
            CpuArchitecture.X64,
            gpuVendor: "NVIDIA");

        OsConditionMatcher.Matches(new[] { new OsCondition { GpuVendor = "nvidia" } }, nvidiaEnv).Should().BeTrue();
        OsConditionMatcher.Matches(new[] { new OsCondition { GpuVendor = "AMD" } }, nvidiaEnv).Should().BeFalse();
    }

    [Fact]
    public void Matches_OsNameMismatch_ShouldReturnFalse()
    {
        OsConditionMatcher.Matches(new[] { new OsCondition { OsName = "Linux" } }, _environment).Should().BeFalse();
        OsConditionMatcher.Matches(new[] { new OsCondition { OsName = "windows" } }, _environment).Should().BeTrue();
    }

    [Fact]
    public void Matches_MultipleConditions_ShouldRequireAll()
    {
        var allMatch = new[]
        {
            new OsCondition { OsName = "Windows", MinBuild = 22000 },
            new OsCondition { CpuArchitecture = "X64" },
        };
        var oneMismatch = new[]
        {
            new OsCondition { OsName = "Windows", MinBuild = 22000 },
            new OsCondition { CpuArchitecture = "Arm64" },
        };

        OsConditionMatcher.Matches(allMatch, _environment).Should().BeTrue();
        OsConditionMatcher.Matches(oneMismatch, _environment).Should().BeFalse();
    }

    [Fact]
    public void WindowsSystemEnvironment_ShouldReportRealOsVersion()
    {
        var environment = new WindowsSystemEnvironment();

        environment.OsVersion.Name.Should().Be("Windows");
        environment.OsVersion.Build.Should().BeGreaterThan(0);
        environment.CpuArchitecture.Should().NotBe(CpuArchitecture.Unknown);
    }
}
