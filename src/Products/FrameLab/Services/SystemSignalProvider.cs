// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PcOpt.Core.Platform;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>系统状态信号（诊断输入的可获得部分；缺失项保守为 false，报告需如实说明）。</summary>
/// <param name="StandbyListExpanded">待机列表是否膨胀（无 ETW/原生查询时为 false）。</param>
/// <param name="CpuThrottling">CPU 是否降频（无频率监控时为 false）。</param>
/// <param name="GpuBottleneck">是否 GPU 瓶颈（由采样数据推断，见调用方）。</param>
/// <param name="HagsEnabled">HAGS 是否开启。</param>
/// <param name="MpoEnabled">MPO 是否开启。</param>
/// <param name="GpuVendor">GPU 厂商。</param>
public sealed record SystemSignals(
    bool StandbyListExpanded,
    bool CpuThrottling,
    bool GpuBottleneck,
    bool HagsEnabled,
    bool MpoEnabled,
    string GpuVendor);

/// <summary>系统状态信号提供者（抽出来便于单测替换）。</summary>
public interface ISystemSignalProvider
{
    /// <summary>读取当前系统状态信号（失败项保守返回默认值，绝不抛异常）。</summary>
    SystemSignals Read();
}

/// <summary>
/// Windows 系统状态信号读取：HAGS（<c>HwSchMode</c>=2 为开启）、MPO
/// （<c>Dwm\OverlayTestMode</c>=5 为关闭，否则视为开启）、GPU 厂商（内核 <see cref="ISystemEnvironment"/>）。
/// 待机列表 / CPU 降频需要 ETW 或原生查询，本版本保守返回 false 并在报告中说明（不夸大）。
/// </summary>
public sealed class WindowsSystemSignalProvider : ISystemSignalProvider
{
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string HagsValueName = "HwSchMode";
    private const string DwmKey = @"SOFTWARE\Microsoft\Windows\Dwm";
    private const string MpoValueName = "OverlayTestMode";
    private const int MpoDisabledMarker = 5;

    private readonly ISystemEnvironment _environment;
    private readonly ILogger<WindowsSystemSignalProvider> _logger;

    /// <summary>初始化信号提供者。</summary>
    public WindowsSystemSignalProvider(ISystemEnvironment environment, ILogger<WindowsSystemSignalProvider> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public SystemSignals Read()
    {
        return new SystemSignals(
            StandbyListExpanded: false,
            CpuThrottling: false,
            GpuBottleneck: false,
            HagsEnabled: IsHagsEnabled(),
            MpoEnabled: IsMpoEnabled(),
            GpuVendor: _environment.GpuVendor ?? string.Empty);
    }

    private bool IsHagsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversKey);
            return key?.GetValue(HagsValueName) is int mode && mode == 2;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _logger.LogDebug("读取 HAGS 状态失败：{Error}", ex.Message);
            return false;
        }
    }

    private bool IsMpoEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DwmKey);
            // 仅当显式写入 OverlayTestMode=5 时视为 MPO 被禁用；否则默认开启
            return key?.GetValue(MpoValueName) is not int mode || mode != MpoDisabledMarker;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _logger.LogDebug("读取 MPO 状态失败：{Error}", ex.Message);
            return true;
        }
    }
}
