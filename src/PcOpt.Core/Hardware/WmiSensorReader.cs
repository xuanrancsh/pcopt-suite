// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Hardware;

/// <summary>
/// WMI 传感器读取器（架构 3.3：LHM 失败时的降级方案）。
/// 数据源 <c>root\wmi</c> 的 <c>MSAcpi_ThermalZoneTemperature</c>：
/// 仅 ACPI 热区温度（无功耗），且桌面机常无此 WMI 类（可用性有限，
/// 界面据此明示"部分传感器不可用"——待明确事项 #2）。
/// 返回值：ACPI 热区最高温度（摄氏）；无实例 / 不支持时返回 null（不可用）。
/// </summary>
public sealed class WmiSensorReader : ISensorReader
{
    /// <summary>MSAcpi_ThermalZoneTemperature 的 WMI 命名空间。</summary>
    private const string ThermalZoneScope = @"root\wmi";

    /// <summary>开尔文 → 摄氏偏移（WMI 温度以 0.1K 为单位报告）。</summary>
    private const float KelvinToCelsiusOffset = 2731.5f;

    /// <inheritdoc/>
    public SensorSource Source => SensorSource.Wmi;

    /// <inheritdoc/>
    public SensorSnapshot? Read()
    {
        var rows = WmiGateway.Query(ThermalZoneScope, "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
        var temperatures = new List<float>();
        foreach (var row in rows)
        {
            var raw = WmiGateway.GetInt32(row, "CurrentTemperature");
            if (raw <= 0)
            {
                continue;
            }

            // WMI 单位为 0.1 开尔文：C = (raw / 10) - 273.15 = raw - 2731.5 后除以 10
            temperatures.Add((raw - KelvinToCelsiusOffset) / 10f);
        }

        if (temperatures.Count == 0)
        {
            return null;
        }

        return new SensorSnapshot(temperatures.Max(), null, null, null, SensorSource.Wmi);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // 无托管资源
    }
}
