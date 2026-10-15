// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 采集会话共享存储（T31/T34/T36）：在测试台 / 报告 / 诊断各 ViewModel 之间共享
/// "最近一次采集""基线采集（优化前）"与"前后对比结果"。注册为单例。
/// </summary>
public sealed class CaptureSessionStore
{
    /// <summary>最近一次采集会话。</summary>
    public CaptureSession? Latest { get; private set; }

    /// <summary>基线采集会话（一键优化前的成绩，用于对比）。</summary>
    public CaptureSession? Baseline { get; private set; }

    /// <summary>最近一次前后对比结果。</summary>
    public ComparisonResult? Comparison { get; private set; }

    /// <summary>任一状态更新时触发（供 UI 刷新）。</summary>
    public event EventHandler? Changed;

    /// <summary>记录最近一次采集（同时作为新的基线若此前无基线）。</summary>
    public void SetLatest(CaptureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Latest = session;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>设置基线（优化前）会话。</summary>
    public void SetBaseline(CaptureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Baseline = session;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>设置前后对比结果。</summary>
    public void SetComparison(ComparisonResult? comparison)
    {
        Comparison = comparison;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
