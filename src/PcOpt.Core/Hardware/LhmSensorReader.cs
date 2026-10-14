// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using LibreHardwareMonitor.Hardware;

namespace PcOpt.Core.Hardware;

/// <summary>
/// LibreHardwareMonitorLib 传感器读取器（架构 3.3：传感器首选）。
/// 说明：
/// <list type="item">
/// <item>MPL-2.0 依赖以 NuGet 方式调用、未修改源码（NOTICE 已登记）；</item>
/// <item>打开失败 / 无权限 / 传感器缺失一律返回 null（调用方降级 WMI），绝不扩散异常；</item>
/// <item>温度与功耗无读数时如实返回 null，不填 0（PRD 5.3 不撒谎原则）。</item>
/// </list>
/// </summary>
public sealed class LhmSensorReader : ISensorReader
{
    private readonly Computer _computer;
    private bool _opened;
    private bool _disposed;

    /// <summary>初始化读取器。</summary>
    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsMotherboardEnabled = true,
        };
    }

    /// <inheritdoc/>
    public SensorSource Source => SensorSource.LibreHardwareMonitor;

    /// <inheritdoc/>
    public SensorSnapshot? Read()
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            if (!_opened)
            {
                _computer.Open();
                _opened = true;
            }

            float? cpuTemperature = null;
            float? cpuPower = null;
            float? gpuTemperature = null;
            float? gpuPower = null;

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                Collect(hardware, ref cpuTemperature, ref cpuPower, ref gpuTemperature, ref gpuPower);
            }

            // 任一读数存在即视为可用；全部为 null 视为不可用（降级 WMI）
            if (cpuTemperature is null && cpuPower is null && gpuTemperature is null && gpuPower is null)
            {
                return null;
            }

            return new SensorSnapshot(cpuTemperature, cpuPower, gpuTemperature, gpuPower, SensorSource.LibreHardwareMonitor);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
        {
            // LHM 在部分平台（驱动缺失 / 内核驱动被安全软件拦截）打开即失败：按不可用处理
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_opened)
            {
                _computer.Close();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // 关闭失败不影响进程退出
        }
    }

    private static void Collect(
        IHardware hardware,
        ref float? cpuTemperature,
        ref float? cpuPower,
        ref float? gpuTemperature,
        ref float? gpuPower)
    {
        var isCpu = hardware.HardwareType == HardwareType.Cpu;
        // 注意：LibreHardwareMonitorLib 0.9.6（NuGet 裁剪版）的 HardwareType 仅含
        // GpuNvidia / GpuAmd，无 Intel GPU 传感器——Intel 集显温度在本库不可用，
        // 由界面"部分传感器不可用"提示覆盖（架构 3.3 / 待明确事项 #2）。
        var isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd;

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value)
            {
                continue;
            }

            if (isCpu && sensor.SensorType == SensorType.Temperature && cpuTemperature is null)
            {
                // 优先整包温度（Package / Tctl / Tdie），否则取首个核心温度
                var isPackage = sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                    || sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                    || sensor.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase);
                if (isPackage || cpuTemperature is null)
                {
                    cpuTemperature = value;
                }
            }

            if (isCpu && sensor.SensorType == SensorType.Power && cpuPower is null
                && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            {
                cpuPower = value;
            }

            if (isGpu && sensor.SensorType == SensorType.Temperature && gpuTemperature is null)
            {
                gpuTemperature = value;
            }

            if (isGpu && sensor.SensorType == SensorType.Power && gpuPower is null
                && (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                    || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
            {
                gpuPower = value;
            }
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            subHardware.Update();
            Collect(subHardware, ref cpuTemperature, ref cpuPower, ref gpuTemperature, ref gpuPower);
        }
    }
}
