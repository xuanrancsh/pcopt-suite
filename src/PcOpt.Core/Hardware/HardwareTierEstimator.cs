// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Hardware;

/// <summary>
/// 硬件档位估算器（T10 Profile 按 gameId × hardwareTier 存储的第二维）。
/// 纯函数：仅基于静态硬件（GPU 显存 + CPU 逻辑处理器数），不读实时负载、不联网。
/// 阈值为保守估计（文档化常量），识别不到 GPU 时返回 <see cref="HardwareTier.Unknown"/>。
/// </summary>
public static class HardwareTierEstimator
{
    /// <summary>Ultra 档 GPU 显存下限（字节）：12 GB。</summary>
    private const long UltraGpuMemoryBytes = 12L * 1024 * 1024 * 1024;

    /// <summary>High 档 GPU 显存下限（字节）：8 GB。</summary>
    private const long HighGpuMemoryBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>Mid 档 GPU 显存下限（字节）：4 GB。</summary>
    private const long MidGpuMemoryBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>Ultra 档 CPU 逻辑处理器下限。</summary>
    private const int UltraLogicalProcessors = 12;

    /// <summary>High 档 CPU 逻辑处理器下限。</summary>
    private const int HighLogicalProcessors = 8;

    /// <summary>Mid 档 CPU 逻辑处理器下限。</summary>
    private const int MidLogicalProcessors = 4;

    /// <summary>估算硬件档位。</summary>
    public static HardwareTier Estimate(HardwareProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var gpu = profile.PrimaryGpu;
        if (gpu is null || gpu.Vendor == GpuVendor.Unknown)
        {
            return HardwareTier.Unknown;
        }

        var memory = gpu.VideoMemoryBytes ?? 0;
        var logical = profile.Cpu.LogicalProcessors;

        if (memory >= UltraGpuMemoryBytes && logical >= UltraLogicalProcessors)
        {
            return HardwareTier.Ultra;
        }

        if (memory >= HighGpuMemoryBytes && logical >= HighLogicalProcessors)
        {
            return HardwareTier.High;
        }

        if (memory >= MidGpuMemoryBytes || logical >= MidLogicalProcessors)
        {
            return HardwareTier.Mid;
        }

        return HardwareTier.Low;
    }
}
