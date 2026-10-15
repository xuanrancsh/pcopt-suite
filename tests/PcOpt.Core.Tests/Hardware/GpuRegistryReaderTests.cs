// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Compliance;
using PcOpt.Core.Hardware;

namespace PcOpt.Core.Tests.Hardware;

/// <summary>
/// <see cref="GpuRegistryReader"/> 测试（T6）：
/// 厂商推断纯函数 + 合规红线 #1 审计（读取路径必须落在写保护区内——只读的证明）。
/// </summary>
/// <remarks>
/// 标记为 <c>RequiresRealHardware</c> 的用例读取<strong>真实显示适配器</strong>，
/// 必须在有真实硬件的桌面机器上运行；无头 CI 虚拟机（GitHub <c>windows-latest</c> 等）
/// 的显示适配器类键为空，这些用例必然失败，因此在 CI 中以
/// <c>--filter "Category!=RequiresRealHardware"</c> 排除，但仍会在本地全量运行中被执行
/// （不删除、不弱化断言）。
/// </remarks>
public sealed class GpuRegistryReaderTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia)]
    [InlineData("NVIDIA GeForce RTX 4070 Laptop GPU", GpuVendor.Nvidia)]
    [InlineData("GeForce GTX 1060 6GB", GpuVendor.Nvidia)]
    [InlineData("NVIDIA RTX A2000", GpuVendor.Nvidia)]
    [InlineData("AMD Radeon RX 7800 XT", GpuVendor.Amd)]
    [InlineData("AMD Radeon Graphics", GpuVendor.Amd)]
    [InlineData("ATI Radeon HD 5770", GpuVendor.Amd)]
    [InlineData("Intel(R) UHD Graphics 630", GpuVendor.Intel)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", GpuVendor.Intel)]
    [InlineData("UHD Graphics 620", GpuVendor.Intel)]
    [InlineData("Iris(R) Xe Graphics", GpuVendor.Intel)]
    [InlineData("  NVIDIA GeForce RTX 5090  ", GpuVendor.Nvidia)]
    public void InferVendor_ShouldMapKnownAdapterStrings(string adapterString, GpuVendor expected)
    {
        GpuRegistryReader.InferVendor(adapterString).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown Display Adapter")]
    [InlineData("Matrox Graphics")]
    public void InferVendor_ShouldReturnUnknown_WhenCannotInfer(string? adapterString)
    {
        // 合规红线 #1：宁可未知，绝不猜测
        GpuRegistryReader.InferVendor(adapterString).Should().Be(GpuVendor.Unknown);
    }

    [Fact]
    public void DisplayAdapterClassPath_ShouldBeInsideComplianceReadOnlyZone()
    {
        // 审计：GPU 读取路径必须同时是 ComplianceGuard 的写保护路径——
        // 证明"读"只可能发生在"写被拦截"的同一区域，红线 #1 双向闭合
        var guard = ComplianceGuard.EnsureWritable(GpuRegistryReader.DisplayAdapterClassPath);

        guard.IsSuccess.Should().BeFalse("显卡类键必须写保护");
        guard.Error.Should().Contain("只读");
    }

    [Fact]
    [Trait("Category", "RequiresRealHardware")]
    public void ReadDisplayAdapters_OnRealMachine_ShouldNotThrow_AndReturnPlausibleList()
    {
        // 需真机：断言依赖"本机至少一块显示适配器"（adapters 非空）；
        // 无头 CI 虚拟机没有显示适配器，无法满足该前置条件，故归入 RequiresRealHardware。
        var reader = new GpuRegistryReader();

        var act = () => reader.ReadDisplayAdapters();

        act.Should().NotThrow();
        var adapters = reader.ReadDisplayAdapters();
        adapters.Should().NotBeNull();
        // 本机至少一块显示适配器（集显亦算）；型号串非空
        adapters.Should().NotBeEmpty();
        adapters.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Name));
        adapters.Should().OnlyContain(a => a.VideoMemoryBytes == null || a.VideoMemoryBytes > 0);
    }
}
