// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;

namespace PcOpt.Core.Hardware;

/// <summary>
/// 硬件检测服务接口（架构 3.3）。
/// 实现必须只读（WMI / 注册表只读 / 传感器库），不写入任何系统状态。
/// </summary>
public interface IHardwareDetectionService
{
    /// <summary>
    /// 检测硬件画像。传感器不可用时优雅降级（<see cref="SensorSource.Unavailable"/>），
    /// 不因传感器失败而整体失败（架构 3.3 / 待明确事项 #2）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<HardwareProfile>> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 传感器读取器抽象（架构 3.3：LHM 优先、WMI 降级的扩展点）。
/// </summary>
public interface ISensorReader : IDisposable
{
    /// <summary>本读取器的数据来源。</summary>
    SensorSource Source { get; }

    /// <summary>
    /// 读取一次传感器快照。
    /// 返回 null 表示本读取器不可用（调用方应降级到下一个）；
    /// 返回快照但部分值为 null 表示该传感器在本机无读数（如实反映，不填 0）。
    /// </summary>
    SensorSnapshot? Read();
}

/// <summary>
/// 传感器链：首选 <see cref="SensorSource.LibreHardwareMonitor"/>，失败降级 WMI，
/// 再失败降级 <see cref="SensorSource.Unavailable"/>（架构 3.3）。
/// </summary>
public sealed class SensorReaderChain : ISensorReader
{
    private readonly IReadOnlyList<ISensorReader> _readers;
    private readonly ILogger<SensorReaderChain> _logger;

    /// <summary>初始化传感器链。</summary>
    /// <param name="readers">按优先级排列的读取器（首选在前）。</param>
    /// <param name="logger">日志。</param>
    public SensorReaderChain(IReadOnlyList<ISensorReader> readers, ILogger<SensorReaderChain> logger)
    {
        _readers = readers ?? throw new ArgumentNullException(nameof(readers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public SensorSource Source => SensorSource.LibreHardwareMonitor;

    /// <inheritdoc/>
    public SensorSnapshot? Read()
    {
        foreach (var reader in _readers)
        {
            SensorSnapshot? snapshot;
            try
            {
                snapshot = reader.Read();
            }
            catch (Exception ex)
            {
                // 传感器库异常绝不扩散：记录后降级到下一个读取器
                _logger.LogDebug(ex, "传感器读取器 {Reader} 异常，降级到下一个", reader.GetType().Name);
                continue;
            }

            if (snapshot is not null)
            {
                return snapshot;
            }

            _logger.LogDebug("传感器读取器 {Reader} 不可用，降级到下一个", reader.GetType().Name);
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var reader in _readers)
        {
            try
            {
                reader.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "释放传感器读取器 {Reader} 失败", reader.GetType().Name);
            }
        }
    }
}
