// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PcOpt.Core.Hardware;

/// <summary>
/// 硬件检测服务（架构 3.3）。
/// 组成：WMI（Win32_Processor / Win32_PhysicalMemory / Win32_DiskDrive / MSStorageDriver_FailurePredictStatus）
/// + 注册表显卡类键只读 + 传感器链（LHM → WMI → Unavailable）。
/// 设计约束：
/// <list type="bullet">
/// <item>全部只读：不写注册表、不改服务、不注入进程（合规红线 #1-#4）；</item>
/// <item>优雅降级：WMI 无数据 / 传感器不可用均不导致整体失败，按"未知"如实反映；</item>
/// <item>可测试：静态硬件读取器与传感器链均可注入替身（单测不依赖真实传感器全成功）。</item>
/// </list>
/// </summary>
public sealed class HardwareDetectionService : IHardwareDetectionService
{
    private readonly ISensorReader _sensorReader;
    private readonly IGpuInfoReader _gpuReader;
    private readonly ILogger<HardwareDetectionService> _logger;

    /// <summary>初始化检测服务。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="sensorReader">传感器链；为 null 时使用 LHM → WMI 默认链。</param>
    /// <param name="gpuReader">GPU 信息读取器；为 null 时使用注册表只读读取器。</param>
    public HardwareDetectionService(
        ILogger<HardwareDetectionService> logger,
        ISensorReader? sensorReader = null,
        IGpuInfoReader? gpuReader = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sensorReader = sensorReader ?? new SensorReaderChain(
            new ISensorReader[] { new LhmSensorReader(), new WmiSensorReader() },
            NullLogger<SensorReaderChain>.Instance);
        _gpuReader = gpuReader ?? new GpuRegistryReader();
    }

    /// <inheritdoc/>
    public async Task<Result<HardwareProfile>> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(
                () => DetectInternal(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result<HardwareProfile>.Fail("硬件检测已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "硬件检测失败");
            return Result<HardwareProfile>.Fail($"硬件检测失败：{ex.Message}");
        }
    }

    private Result<HardwareProfile> DetectInternal(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cpu = ReadCpu();
        var gpus = ReadGpusSafely();
        var memory = ReadMemory();
        var disks = ReadDisks();
        var sensors = ReadSensorsSafely();

        return Result<HardwareProfile>.Ok(new HardwareProfile(
            cpu,
            gpus,
            memory,
            disks,
            sensors,
            DateTime.UtcNow));
    }

    private static CpuInfo ReadCpu()
    {
        var rows = WmiGateway.Query(@"root\cimv2", "SELECT * FROM Win32_Processor");
        var row = rows.Count > 0 ? rows[0] : null;

        var name = row is null ? null : WmiGateway.GetString(row, "Name");
        var physicalCores = row is null ? 0 : WmiGateway.GetInt32(row, "NumberOfCores");
        var logicalProcessors = row is null ? 0 : WmiGateway.GetInt32(row, "NumberOfLogicalProcessors");
        var maxClock = row is null ? 0 : WmiGateway.GetInt32(row, "MaxClockSpeed");
        // Win32_Processor 的 L2CacheSize / L3CacheSize 单位为 KB
        var l2 = row is null ? 0 : WmiGateway.GetInt32(row, "L2CacheSize");
        var l3 = row is null ? 0 : WmiGateway.GetInt32(row, "L3CacheSize");

        // 大小核拓扑（Win10 不支持时按不可用处理，回退 WMI 核心数）
        var topology = ProcessorTopologyReader.Read();
        if (topology.PhysicalCores > 0)
        {
            physicalCores = topology.PhysicalCores;
        }

        if (topology.LogicalProcessors > 0)
        {
            logicalProcessors = topology.LogicalProcessors;
        }

        return new CpuInfo(
            string.IsNullOrWhiteSpace(name) ? "未知 CPU" : name.Trim(),
            DescribeArchitecture(),
            physicalCores,
            logicalProcessors,
            topology.PerformanceCores,
            topology.EfficiencyCores,
            maxClock,
            l2,
            l3);
    }

    private IReadOnlyList<GpuInfo> ReadGpusSafely()
    {
        try
        {
            return _gpuReader.ReadGpus();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GPU 信息读取失败，按未知处理");
            return Array.Empty<GpuInfo>();
        }
    }

    private static MemoryInfo ReadMemory()
    {
        var rows = WmiGateway.Query(@"root\cimv2", "SELECT Capacity, Speed FROM Win32_PhysicalMemory");
        long total = 0;
        var moduleCount = 0;
        int? speed = null;
        foreach (var row in rows)
        {
            total += WmiGateway.GetInt64(row, "Capacity");
            moduleCount++;
            var moduleSpeed = WmiGateway.GetInt32(row, "Speed");
            if (moduleSpeed > 0)
            {
                speed ??= moduleSpeed;
            }
        }

        if (total <= 0)
        {
            // 降级：Win32_ComputerSystem.TotalPhysicalMemory
            var systemRows = WmiGateway.Query(@"root\cimv2", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            if (systemRows.Count > 0)
            {
                total = WmiGateway.GetInt64(systemRows[0], "TotalPhysicalMemory");
            }
        }

        return new MemoryInfo(total, speed, moduleCount);
    }

    private static IReadOnlyList<DiskInfo> ReadDisks()
    {
        var disks = WmiGateway.Query(
            @"root\cimv2",
            "SELECT Model, Size, InterfaceType, DeviceID FROM Win32_DiskDrive");
        var failing = ReadSmartFailingDevices();

        var result = new List<DiskInfo>(disks.Count);
        foreach (var row in disks)
        {
            var model = WmiGateway.GetString(row, "Model") ?? string.Empty;
            var size = WmiGateway.GetInt64(row, "Size");
            var interfaceType = WmiGateway.GetString(row, "InterfaceType");
            var deviceId = WmiGateway.GetString(row, "DeviceID");

            var smart = SmartStatus.Unknown;
            if (failing.Count > 0 && deviceId is not null)
            {
                // MSStorageDriver_FailurePredictStatus.InstanceName 与 DiskDrive.DeviceID 均形如 \\.\PHYSICALDRIVE0
                var normalized = NormalizeDeviceId(deviceId);
                if (failing.TryGetValue(normalized, out var predictsFailure))
                {
                    smart = predictsFailure ? SmartStatus.PredictedFailure : SmartStatus.Ok;
                }
            }

            result.Add(new DiskInfo(model, size, interfaceType, smart));
        }

        return result;
    }

    /// <summary>读取 SMART 预测失败状态（root\wmi MSStorageDriver_FailurePredictStatus）。</summary>
    private static Dictionary<string, bool> ReadSmartFailingDevices()
    {
        var rows = WmiGateway.Query(
            @"root\wmi",
            "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var instanceName = WmiGateway.GetString(row, "InstanceName");
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                continue;
            }

            map[NormalizeDeviceId(instanceName)] = WmiGateway.GetInt32(row, "PredictFailure") != 0;
        }

        return map;
    }

    private SensorSnapshot ReadSensorsSafely()
    {
        SensorSnapshot? snapshot;
        try
        {
            snapshot = _sensorReader.Read();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "传感器链读取失败");
            snapshot = null;
        }

        return snapshot ?? SensorSnapshot.Empty(SensorSource.Unavailable);
    }

    /// <summary>规范化设备 Id：\\.\PHYSICALDRIVE0 → physicaldrive0（大小写 / 前缀无关）。</summary>
    private static string NormalizeDeviceId(string deviceId) =>
        deviceId.Trim().Replace(@"\\.\", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>当前进程架构描述（不依赖 WMI Architecture 字段语义差异）。</summary>
    private static string DescribeArchitecture() => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "X64",
        System.Runtime.InteropServices.Architecture.X86 => "X86",
        System.Runtime.InteropServices.Architecture.Arm64 => "Arm64",
        System.Runtime.InteropServices.Architecture.Arm => "Arm",
        _ => "Unknown",
    };
}

/// <summary>GPU 信息读取器抽象（便于单测注入）。</summary>
public interface IGpuInfoReader
{
    /// <summary>读取 GPU 列表（只读）。</summary>
    IReadOnlyList<GpuInfo> ReadGpus();
}
