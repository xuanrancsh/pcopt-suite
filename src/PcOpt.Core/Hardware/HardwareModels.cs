// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Hardware;

/// <summary>
/// GPU 厂商（合规红线 #1：仅真实识别，绝不做型号伪装）。
/// </summary>
public enum GpuVendor
{
    /// <summary>未能识别（保持未知，不猜测）。</summary>
    Unknown = 0,

    /// <summary>NVIDIA。</summary>
    Nvidia,

    /// <summary>AMD / ATI。</summary>
    Amd,

    /// <summary>Intel（集显 / 锐炫）。</summary>
    Intel,
}

/// <summary>磁盘 SMART 预测状态（WMI MSStorageDriver_FailurePredictStatus）。</summary>
public enum SmartStatus
{
    /// <summary>无 SMART 数据或驱动不支持（不猜测健康）。</summary>
    Unknown = 0,

    /// <summary>SMART 正常（PredictFailure = false）。</summary>
    Ok,

    /// <summary>SMART 预测故障（PredictFailure = true）。</summary>
    PredictedFailure,
}

/// <summary>传感器数据来源（界面据此提示"部分传感器不可用"，架构 3.3）。</summary>
public enum SensorSource
{
    /// <summary>传感器全部不可用（虚拟机 / 老旧平台 / LHM 与 WMI 均无读数）。</summary>
    Unavailable = 0,

    /// <summary>Windows Management Instrumentation（ACPI 热区，覆盖率有限）。</summary>
    Wmi,

    /// <summary>LibreHardwareMonitorLib（首选，覆盖率最高）。</summary>
    LibreHardwareMonitor,
}

/// <summary>
/// 硬件档位估算（T10 Profile 按 gameId × hardwareTier 存储的第二维）。
/// 仅基于静态硬件（GPU 显存 + CPU 逻辑处理器数）的保守估计，不读实时负载。
/// </summary>
public enum HardwareTier
{
    /// <summary>无法估算（未识别到 GPU）。</summary>
    Unknown = 0,
    Low,
    Mid,
    High,
    Ultra,
}

/// <summary>CPU 信息（架构 3.3：WMI Win32_Processor + GetLogicalProcessorInformationEx 大小核拓扑）。</summary>
/// <param name="Name">型号（如 "13th Gen Intel(R) Core(TM) i7-13700H"）。</param>
/// <param name="Architecture">CPU 架构（X64 / Arm64 / X86）。</param>
/// <param name="PhysicalCores">物理核心数。</param>
/// <param name="LogicalProcessors">逻辑处理器数（含超线程）。</param>
/// <param name="PerformanceCores">性能核数；大小核拓扑不可用时为 null（如 Windows 10）。</param>
/// <param name="EfficiencyCores">能效核数；大小核拓扑不可用时为 null。</param>
/// <param name="MaxClockMHz">最大主频（MHz）。</param>
/// <param name="L2CacheKB">L2 缓存（KB）。</param>
/// <param name="L3CacheKB">L3 缓存（KB）。</param>
public sealed record CpuInfo(
    string Name,
    string Architecture,
    int PhysicalCores,
    int LogicalProcessors,
    int? PerformanceCores,
    int? EfficiencyCores,
    int MaxClockMHz,
    int L2CacheKB,
    int L3CacheKB);

/// <summary>GPU 信息（注册表显卡类键只读解析，合规红线 #1）。</summary>
/// <param name="Vendor">厂商；无法从型号字符串推断时为 <see cref="GpuVendor.Unknown"/>。</param>
/// <param name="Name">适配器型号字符串（HardwareInformation.AdapterString）。</param>
/// <param name="VideoMemoryBytes">显存字节数；驱动未报告时为 null（不猜测）。</param>
/// <param name="DriverVersion">驱动版本；不可用时为 null。</param>
public sealed record GpuInfo(GpuVendor Vendor, string Name, long? VideoMemoryBytes, string? DriverVersion);

/// <summary>内存信息（WMI Win32_PhysicalMemory）。</summary>
/// <param name="TotalBytes">总物理内存字节数。</param>
/// <param name="SpeedMHz">内存频率（MHz）；不可用时为 null。</param>
/// <param name="ModuleCount">物理内存条数。</param>
public sealed record MemoryInfo(long TotalBytes, int? SpeedMHz, int ModuleCount);

/// <summary>磁盘信息（WMI Win32_DiskDrive + SMART 预测状态）。</summary>
/// <param name="Model">磁盘型号。</param>
/// <param name="SizeBytes">容量字节数。</param>
/// <param name="InterfaceType">接口类型（USB / SCSI / NVMe 等）；不可用时为 null。</param>
/// <param name="Smart">SMART 预测状态。</param>
public sealed record DiskInfo(string Model, long SizeBytes, string? InterfaceType, SmartStatus Smart);

/// <summary>传感器读数快照（温度 / 功耗；任一无读数即为 null，不填 0 撒谎）。</summary>
/// <param name="CpuTemperatureC">CPU 温度（摄氏度）。</param>
/// <param name="CpuPackagePowerW">CPU 封装功耗（瓦）。</param>
/// <param name="GpuTemperatureC">GPU 温度（摄氏度）。</param>
/// <param name="GpuPackagePowerW">GPU 封装功耗（瓦）。</param>
/// <param name="Source">数据来源；全部不可用时为 <see cref="SensorSource.Unavailable"/>。</param>
public sealed record SensorSnapshot(
    float? CpuTemperatureC,
    float? CpuPackagePowerW,
    float? GpuTemperatureC,
    float? GpuPackagePowerW,
    SensorSource Source)
{
    /// <summary>空快照（无任何读数）。</summary>
    public static SensorSnapshot Empty(SensorSource source) => new(null, null, null, null, source);
}

/// <summary>
/// 硬件画像（不可变记录，架构 3.3 输出契约；供三产品消费）。
/// 全部字段来自只读探测：WMI + 注册表只读 + 传感器库；不含任何用户标识信息。
/// </summary>
/// <param name="Cpu">CPU 信息。</param>
/// <param name="Gpus">GPU 列表（独显 + 集显均如实列出）。</param>
/// <param name="Memory">内存信息。</param>
/// <param name="Disks">磁盘列表。</param>
/// <param name="Sensors">传感器快照。</param>
/// <param name="DetectedAtUtc">检测时间（UTC）。</param>
public sealed record HardwareProfile(
    CpuInfo Cpu,
    IReadOnlyList<GpuInfo> Gpus,
    MemoryInfo Memory,
    IReadOnlyList<DiskInfo> Disks,
    SensorSnapshot Sensors,
    DateTime DetectedAtUtc)
{
    /// <summary>首选 GPU（优先独显：NVIDIA / AMD，其次 Intel，最后第一个）。</summary>
    public GpuInfo? PrimaryGpu =>
        Gpus.FirstOrDefault(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd)
        ?? Gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Intel)
        ?? Gpus.FirstOrDefault();

    /// <summary>传感器是否可用（界面据此明示"部分传感器不可用"）。</summary>
    public bool SensorsAvailable => Sensors.Source != SensorSource.Unavailable;
}
