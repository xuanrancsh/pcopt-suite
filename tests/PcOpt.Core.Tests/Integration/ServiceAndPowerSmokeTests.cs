// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tests.Integration;

/// <summary>
/// 系统服务 / 电源计划只读冒烟测试（Trait: Integration）。
/// 仅执行读取操作（SCM 查询、powercfg /list、/getactivescheme），不修改任何系统状态。
/// </summary>
[Trait("Integration", "System")]
public sealed class ServiceAndPowerSmokeTests
{
    [Fact]
    public void GetStartType_KnownService_ShouldReturnValidMode()
    {
        var control = new ServiceControl();

        var result = control.GetStartType("Schedule");

        result.IsSuccess.Should().BeTrue(result.Error);
        Enum.IsDefined(result.Value!).Should().BeTrue();
    }

    [Fact]
    public void GetStartType_NonExistentService_ShouldFailWithClearError()
    {
        var control = new ServiceControl();

        var result = control.GetStartType("pcopt-no-such-service");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("无法打开服务");
    }

    [Fact]
    public void ListSchemes_ShouldReturnAtLeastOneScheme()
    {
        var controller = new PowerCfgController();

        var result = controller.ListSchemes();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Count.Should().BeGreaterThan(0);
        result.Value!.Should().OnlyContain(s => s.Guid != Guid.Empty);
    }

    [Fact]
    public void GetActiveSchemeGuid_ShouldReturnValidGuid()
    {
        var controller = new PowerCfgController();

        var result = controller.GetActiveSchemeGuid();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ListSchemes_ShouldMarkExactlyOneActive()
    {
        var controller = new PowerCfgController();

        var schemes = controller.ListSchemes().Value!;
        var active = controller.GetActiveSchemeGuid().Value;

        schemes.Count(s => s.Guid == active).Should().Be(1);
    }
}
