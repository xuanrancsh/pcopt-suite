// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Native;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;

namespace PcOpt.Core.Tests.Integration;

/// <summary>
/// 计时器分辨率冒烟测试（Trait: Integration）。
/// 请求 0.5ms 后立即还原原值；计时器请求为进程级，测试退出时系统亦会回收。
/// </summary>
[Trait("Integration", "Timer")]
public sealed class TimerResolutionSmokeTests
{
    private readonly TimerResolutionController _controller = new();
    private readonly TimerResolutionActionHandler _handler;

    public TimerResolutionSmokeTests()
    {
        _handler = new TimerResolutionActionHandler(
            _controller,
            NullLogger<TimerResolutionActionHandler>.Instance);
    }

    [Fact]
    public void QueryResolution_ShouldReturnSaneRange()
    {
        var range = _controller.QueryResolution();

        range.IsSuccess.Should().BeTrue(range.Error);
        var (minimum, maximum, current) = range.Value;
        minimum.Should().BeGreaterThan(0);
        maximum.Should().BeGreaterThanOrEqualTo(minimum);
        current.Should().BeInRange(minimum, maximum);
    }

    [Fact]
    public void ApplyThenRevert_ShouldRestoreOriginalResolution()
    {
        var action = new TimerResolutionAction(TimerResolutionController.HalfMillisecond100Ns);

        var original = _controller.QueryResolution().Value;
        var snapshot = _handler.CaptureOriginal(action);
        snapshot.IsSuccess.Should().BeTrue(snapshot.Error);

        var apply = _handler.Apply(action);
        apply.IsSuccess.Should().BeTrue(apply.Error);
        _controller.QueryResolution().Value.Current100Ns
            .Should().Be(TimerResolutionController.HalfMillisecond100Ns);

        var revert = _handler.Revert(action, snapshot.Value!);
        revert.IsSuccess.Should().BeTrue(revert.Error);
        _controller.QueryResolution().Value.Current100Ns.Should().Be(original.Current100Ns);
    }

    [Fact]
    public void Set_ResolutionOutsideRange_ShouldFail()
    {
        var (minimum, _, _) = _controller.QueryResolution().Value;

        var result = _controller.Set(minimum - 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("超出系统支持范围");
    }
}
