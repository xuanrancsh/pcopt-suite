// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Hardware;
using PcOpt.Core.Tests.TestDoubles;

namespace PcOpt.Core.Tests.Hardware;

/// <summary><see cref="HardwareTierEstimator"/> 纯函数测试（T6，供 T10 Profile 第二维使用）。</summary>
public sealed class HardwareTierEstimatorTests
{
    private static HardwareProfile BuildProfile(GpuVendor vendor, long? videoMemory, int logicalProcessors) =>
        new(
            new CpuInfo("Test CPU", "X64", logicalProcessors / 2, logicalProcessors, null, null, 3000, 0, 0),
            new[] { new GpuInfo(vendor, "Test GPU", videoMemory, null) },
            new MemoryInfo(16L * 1024 * 1024 * 1024, 3200, 2),
            Array.Empty<DiskInfo>(),
            SensorSnapshot.Empty(SensorSource.Unavailable),
            DateTime.UtcNow);

    [Fact]
    public void Estimate_NoGpu_ShouldBeUnknown()
    {
        var profile = BuildProfile(GpuVendor.Unknown, videoMemory: null, logicalProcessors: 16);

        HardwareTierEstimator.Estimate(profile).Should().Be(HardwareTier.Unknown);
    }

    [Fact]
    public void Estimate_EmptyGpuList_ShouldBeUnknown()
    {
        var profile = BuildProfile(GpuVendor.Nvidia, 16L * 1024 * 1024 * 1024, 16) with { Gpus = Array.Empty<GpuInfo>() };

        HardwareTierEstimator.Estimate(profile).Should().Be(HardwareTier.Unknown);
    }

    [Fact]
    public void Estimate_HighEndMachine_ShouldBeUltra()
    {
        var profile = BuildProfile(GpuVendor.Nvidia, 16L * 1024 * 1024 * 1024, 16);

        HardwareTierEstimator.Estimate(profile).Should().Be(HardwareTier.Ultra);
    }

    [Fact]
    public void Estimate_MidHighMachine_ShouldBeHigh()
    {
        // 8GB 显存 + 8 逻辑处理器 → High（不满足 Ultra 的 12GB/12 线程）
        var profile = BuildProfile(GpuVendor.Nvidia, 8L * 1024 * 1024 * 1024, 8);

        HardwareTierEstimator.Estimate(profile).Should().Be(HardwareTier.High);
    }

    [Fact]
    public void Estimate_ModestMachine_ShouldBeMidOrLow()
    {
        var profile = BuildProfile(GpuVendor.Intel, 512L * 1024 * 1024, 4);

        var tier = HardwareTierEstimator.Estimate(profile);

        tier.Should().BeOneOf(HardwareTier.Mid, HardwareTier.Low);
    }

    [Fact]
    public void Estimate_GpuMemoryUnknown_ShouldNotGuess()
    {
        // 显存未知（null → 0）但 CPU 达标 → Mid；不会因"未知显存"而高估
        var profile = BuildProfile(GpuVendor.Nvidia, videoMemory: null, logicalProcessors: 8);

        HardwareTierEstimator.Estimate(profile).Should().Be(HardwareTier.Mid);
    }
}

/// <summary><see cref="SensorReaderChain"/> 测试（T6：LHM → WMI 降级链）。</summary>
public sealed class SensorReaderChainTests
{
    [Fact]
    public void Chain_FirstReaderUnavailable_ShouldFallToNext()
    {
        var fallback = new FakeSensorReader(new SensorSnapshot(50f, null, null, null, SensorSource.Wmi));
        var chain = new SensorReaderChain(
            new[] { new FakeSensorReader(null), fallback },
            NullLoggerForChain());

        var snapshot = chain.Read();

        snapshot.Should().NotBeNull();
        snapshot!.Source.Should().Be(SensorSource.Wmi);
        snapshot.CpuTemperatureC.Should().Be(50f);
    }

    [Fact]
    public void Chain_FirstReaderThrows_ShouldFallToNext()
    {
        var chain = new SensorReaderChain(
            new[] { new FakeSensorReader(null, throwOnRead: true), new FakeSensorReader(SensorSnapshot.Empty(SensorSource.Wmi)) },
            NullLoggerForChain());

        chain.Read()!.Source.Should().Be(SensorSource.Wmi);
    }

    [Fact]
    public void Chain_AllUnavailable_ShouldReturnNull()
    {
        var chain = new SensorReaderChain(
            new[] { new FakeSensorReader(null), new FakeSensorReader(null) },
            NullLoggerForChain());

        chain.Read().Should().BeNull();
    }

    [Fact]
    public void Chain_PrefersFirstAvailableReader()
    {
        var chain = new SensorReaderChain(
            new[]
            {
                new FakeSensorReader(new SensorSnapshot(60f, 25f, null, null, SensorSource.LibreHardwareMonitor)),
                new FakeSensorReader(new SensorSnapshot(40f, null, null, null, SensorSource.Wmi)),
            },
            NullLoggerForChain());

        var snapshot = chain.Read();

        snapshot!.Source.Should().Be(SensorSource.LibreHardwareMonitor);
        snapshot.CpuTemperatureC.Should().Be(60f);
    }

    private static Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorReaderChain> NullLoggerForChain() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<SensorReaderChain>.Instance;
}
