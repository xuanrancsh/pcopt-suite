// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Native;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
// 别名消歧：内核自有优先级枚举与 System.Diagnostics.ProcessPriorityClass 同名
using ProcessPriorityClass = PcOpt.Core.Tweak.ProcessPriorityClass;

namespace PcOpt.Core.Tests.Integration;

/// <summary>
/// 进程调度冒烟测试（Trait: Integration）。
/// 对<strong>当前测试进程自身</strong>执行 apply → verify → revert，验证 handler 一致性；
/// 全部改动在测试内还原，不波及其他进程。
/// </summary>
[Trait("Integration", "Process")]
[Collection("ProcessScheduling")]
public sealed class ProcessSchedulingSmokeTests
{
    private static readonly string CurrentProcessName =
        Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "dotnet");

    private readonly ProcessPriorityController _controller = new();
    private readonly ProcessPriorityActionHandler _handler;

    public ProcessSchedulingSmokeTests()
    {
        _handler = new ProcessPriorityActionHandler(
            _controller,
            NullLogger<ProcessPriorityActionHandler>.Instance);
    }

    [Fact]
    public void CaptureThenRevert_ShouldRestoreOriginalPriority()
    {
        var action = new ProcessPriorityAction(CurrentProcessName, ProcessPriorityClass.BelowNormal, null);
        var processId = ProcessPriorityActionHandler.FindProcessId(CurrentProcessName);
        processId.Should().NotBeNull("测试进程必须可通过名称找到");

        var original = _controller.Query(processId!.Value).Value!;
        var snapshot = _handler.CaptureOriginal(action);
        snapshot.IsSuccess.Should().BeTrue(snapshot.Error);

        var apply = _handler.Apply(action);
        apply.IsSuccess.Should().BeTrue(apply.Error);
        _handler.Verify(action).IsSuccess.Should().BeTrue();

        var revert = _handler.Revert(action, snapshot.Value!);
        revert.IsSuccess.Should().BeTrue(revert.Error);

        var restored = _controller.Query(processId.Value).Value!;
        restored.Priority.Should().Be(original.Priority);
    }

    [Fact]
    public void Apply_NonExistentProcess_ShouldFailWithClearError()
    {
        var action = new ProcessPriorityAction("pcopt-no-such-process", ProcessPriorityClass.Normal, null);

        var snapshot = _handler.CaptureOriginal(action);
        snapshot.IsSuccess.Should().BeTrue(snapshot.Error);
        snapshot.Value.Should().Contain("\"ProcessExists\":false");

        var apply = _handler.Apply(action);
        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("未运行");

        // 原本不存在的进程：还原为空操作
        var revert = _handler.Revert(action, snapshot.Value!);
        revert.IsSuccess.Should().BeTrue(revert.Error);
    }

    [Fact]
    public void SetAffinity_ShouldRoundTripOnCurrentProcess()
    {
        var processId = Environment.ProcessId;
        var original = _controller.Query(processId).Value!;
        original.AffinityMask.Should().NotBeNull("应能读取当前进程亲和性");

        // 取系统亲和性的最低有效位作为目标掩码（单核机器上等于原值，空操作亦安全）
        var systemAffinity = GetSystemAffinity(processId);
        var target = systemAffinity & (~systemAffinity + 1);

        try
        {
            var set = _controller.SetAffinity(processId, target);
            set.IsSuccess.Should().BeTrue(set.Error);
            _controller.Query(processId).Value!.AffinityMask.Should().Be(target);
        }
        finally
        {
            var restore = _controller.SetAffinity(processId, original.AffinityMask!.Value);
            restore.IsSuccess.Should().BeTrue(restore.Error);
        }
    }

    [Fact]
    public void SetEcoQos_ShouldRoundTripOnCurrentProcess()
    {
        var processId = Environment.ProcessId;
        var original = _controller.Query(processId).Value!;

        if (original.EcoQosEnabled is null)
        {
            // 当前系统不支持查询 EcoQoS 状态（Windows 10：GetProcessInformation 不接纳
            // ProcessPowerThrottling 信息类，微软 Q&A 已确认仅 Set 侧支持）。
            // 降级验证：Set 调用本身成功即可，读取回验跳过（Win11 上自动恢复完整断言）。
            var setOnly = _controller.SetEcoQos(processId, enable: true);
            setOnly.IsSuccess.Should().BeTrue(setOnly.Error);
            return;
        }

        try
        {
            var enable = _controller.SetEcoQos(processId, enable: true);
            enable.IsSuccess.Should().BeTrue(enable.Error);
            _controller.Query(processId).Value!.EcoQosEnabled.Should().BeTrue();
        }
        finally
        {
            var restore = _controller.SetEcoQos(processId, original.EcoQosEnabled!.Value);
            restore.IsSuccess.Should().BeTrue(restore.Error);
        }
    }

    [Fact]
    public void SetAffinity_MaskExceedingSystem_ShouldFail()
    {
        var processId = Environment.ProcessId;
        var systemAffinity = GetSystemAffinity(processId);

        var result = _controller.SetAffinity(processId, (systemAffinity << 1) | 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("超出系统逻辑处理器范围");
    }

    private static long GetSystemAffinity(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, bInheritHandle: false, processId);
        handle.Should().NotBe(IntPtr.Zero);
        try
        {
            var ok = NativeMethods.GetProcessAffinityMask(handle, out _, out var systemAffinity);
            ok.Should().BeTrue();
            return systemAffinity.ToInt64();
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }
}
