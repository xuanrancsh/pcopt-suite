// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Hardware;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>可控传感器读取器替身（返回固定快照 / null / 抛异常）。</summary>
internal sealed class FakeSensorReader : ISensorReader
{
    private readonly SensorSnapshot? _snapshot;
    private readonly bool _throw;

    /// <summary>初始化替身。</summary>
    /// <param name="snapshot">返回的快照；null 表示"不可用"。</param>
    /// <param name="throwOnRead">是否在 Read 时抛异常。</param>
    public FakeSensorReader(SensorSnapshot? snapshot, bool throwOnRead = false)
    {
        _snapshot = snapshot;
        _throw = throwOnRead;
    }

    /// <inheritdoc/>
    public SensorSource Source => SensorSource.Wmi;

    /// <inheritdoc/>
    public SensorSnapshot? Read()
    {
        if (_throw)
        {
            throw new InvalidOperationException("模拟传感器读取异常");
        }

        return _snapshot;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // 无资源
    }
}

/// <summary>可控 GPU 读取器替身。</summary>
internal sealed class FakeGpuReader : IGpuInfoReader
{
    private readonly IReadOnlyList<GpuInfo> _gpus;

    /// <summary>初始化替身。</summary>
    public FakeGpuReader(IReadOnlyList<GpuInfo> gpus)
    {
        _gpus = gpus;
    }

    /// <inheritdoc/>
    public IReadOnlyList<GpuInfo> ReadGpus() => _gpus;
}
