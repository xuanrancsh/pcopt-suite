// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Hardware;
using PcOpt.Core.Tests.TestDoubles;

namespace PcOpt.Core.Tests.Hardware;

/// <summary>
/// <see cref="HardwareDetectionService"/> 测试（T6 架构 3.3）：
/// 本机一致性 + 传感器缺失优雅降级（不依赖真实传感器全成功——待明确事项 #2）。
/// </summary>
public sealed class HardwareDetectionServiceTests
{
    private static HardwareDetectionService CreateService(
        ISensorReader? sensorReader = null,
        IGpuInfoReader? gpuReader = null) =>
        new(NullLogger<HardwareDetectionService>.Instance, sensorReader, gpuReader);

    [Fact]
    public async Task DetectAsync_OnRealMachine_ShouldProduceConsistentProfile()
    {
        var service = CreateService();

        var result = await service.DetectAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var profile = result.Value!;

        // CPU：本机必有合理值
        profile.Cpu.Name.Should().NotBeNullOrWhiteSpace();
        profile.Cpu.PhysicalCores.Should().BeGreaterThan(0);
        profile.Cpu.LogicalProcessors.Should().BeGreaterThanOrEqualTo(profile.Cpu.PhysicalCores);
        profile.Cpu.Architecture.Should().BeOneOf("X64", "X86", "Arm64", "Arm", "Unknown");

        // 大小核：不可用（Win10）为 null；可用时 性能核+能效核 = 逻辑处理器数
        if (profile.Cpu.PerformanceCores is { } p && profile.Cpu.EfficiencyCores is { } e)
        {
            (p + e).Should().Be(profile.Cpu.LogicalProcessors);
        }

        // 内存 / 磁盘
        profile.Memory.TotalBytes.Should().BeGreaterThan(0);
        profile.Disks.Should().NotBeEmpty();

        // 传感器：可用或明确标记不可用（两种都算优雅降级成功）
        profile.SensorsAvailable.Should().Be(profile.Sensors.Source != SensorSource.Unavailable);
        if (profile.SensorsAvailable)
        {
            profile.Sensors.Source.Should().BeOneOf(SensorSource.LibreHardwareMonitor, SensorSource.Wmi);
        }

        // GPU：真实机器至少一块显示适配器；厂商要么识别要么 Unknown（不猜测）
        profile.Gpus.Should().NotBeEmpty();
        profile.Gpus.Should().OnlyContain(g => !string.IsNullOrWhiteSpace(g.Name));
        profile.PrimaryGpu.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectAsync_SensorReaderUnavailable_ShouldDegradeToUnavailable()
    {
        var service = CreateService(sensorReader: new FakeSensorReader(null));

        var result = await service.DetectAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var profile = result.Value!;
        profile.SensorsAvailable.Should().BeFalse();
        profile.Sensors.Source.Should().Be(SensorSource.Unavailable);
        profile.Sensors.CpuTemperatureC.Should().BeNull();

        // 传感器失败不影响静态硬件
        profile.Cpu.Name.Should().NotBeNullOrWhiteSpace();
        profile.Memory.TotalBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DetectAsync_SensorReaderThrows_ShouldDegradeToUnavailable()
    {
        var service = CreateService(sensorReader: new FakeSensorReader(null, throwOnRead: true));

        var result = await service.DetectAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.SensorsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task DetectAsync_WithInjectedGpuReader_ShouldUseInjectedData()
    {
        var gpus = new[]
        {
            new GpuInfo(GpuVendor.Intel, "Intel(R) UHD Graphics", null, null),
            new GpuInfo(GpuVendor.Nvidia, "NVIDIA GeForce RTX 4070 Laptop GPU", 8L * 1024 * 1024 * 1024, "31.0.15.5141"),
        };
        var service = CreateService(
            sensorReader: new FakeSensorReader(SensorSnapshot.Empty(SensorSource.Wmi)),
            gpuReader: new FakeGpuReader(gpus));

        var result = await service.DetectAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var profile = result.Value!;
        profile.Gpus.Should().HaveCount(2);
        // 首选 GPU 优先独显
        profile.PrimaryGpu!.Vendor.Should().Be(GpuVendor.Nvidia);
    }

    [Fact]
    public async Task DetectAsync_WithCancellation_ShouldFail()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.DetectAsync(cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("取消");
    }
}
