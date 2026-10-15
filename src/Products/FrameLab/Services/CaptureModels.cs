// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 单帧样本（对应 PresentMon CSV 的一行，架构 4.3 / PRD C-P0-1）。
/// 全部时间字段单位为毫秒；缺失列以 null 表示（<see cref="PresentMonCsvParser"/> 容错解析）。
/// </summary>
public sealed record FrameSample
{
    /// <summary>应用/进程名（PresentMon 的 Application 列）。</summary>
    public string Application { get; init; } = string.Empty;

    /// <summary>相对采集起点的时刻（秒）。</summary>
    public double TimeInSeconds { get; init; }

    /// <summary>帧时间（毫秒；相邻两次呈现的间隔）。</summary>
    public double FrameTimeMs { get; init; }

    /// <summary>CPU 帧时间近似（毫秒；PresentMon 的 CPU 相关列）。</summary>
    public double? CpuMs { get; init; }

    /// <summary>GPU 忙时（毫秒；PresentMon 的 GPU 相关列）。</summary>
    public double? GpuMs { get; init; }

    /// <summary>显示延迟（毫秒；PresentMon 的 Display latency 相关列）。</summary>
    public double? DisplayLatencyMs { get; init; }
}

/// <summary>PresentMon 可用性探测结果。</summary>
/// <param name="Available">是否可用（可执行文件存在）。</param>
/// <param name="ExecutablePath">可执行文件路径（不可用时为 null）。</param>
/// <param name="Message">人话说明（不可用时给出获取方式，PRD 4.6 / 环境约束）。</param>
public sealed record PresentMonAvailability(bool Available, string? ExecutablePath, string Message);

/// <summary>PresentMon 采集参数。</summary>
/// <param name="ProcessName">目标游戏可执行文件名（如 cs2.exe）；为空表示全屏采集。</param>
/// <param name="DurationSeconds">采集时长（秒）。</param>
/// <param name="OutputCsvPath">CSV 输出路径（调用方负责清理）。</param>
public sealed record PresentMonOptions(string ProcessName, int DurationSeconds, string OutputCsvPath);

/// <summary>PresentMon 一次采集的结果。</summary>
/// <param name="Samples">解析得到的帧样本（按捕获顺序）。</param>
/// <param name="CsvPath">CSV 落盘路径（已清理时为 null）。</param>
/// <param name="Duration">实际采集时长。</param>
/// <param name="StandardError">PresentMon 的标准错误输出（用于诊断；已裁剪）。</param>
public sealed record PresentMonCaptureResult(
    IReadOnlyList<FrameSample> Samples,
    string? CsvPath,
    TimeSpan Duration,
    string StandardError);

/// <summary>一次完整测试会话（捕获 + 分析 + 元数据）。</summary>
/// <param name="SessionId">会话 Id（Guid，去分隔符）。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="ProcessName">目标进程名。</param>
/// <param name="DurationSeconds">采集时长（秒）。</param>
/// <param name="Samples">帧样本。</param>
/// <param name="Stats">分析指标（<see cref="FrameAnalyzer"/> 产出）。</param>
/// <param name="Label">会话标签（如"优化前"/"优化后"，历史库用）。</param>
public sealed record CaptureSession(
    string SessionId,
    DateTime CreatedAtUtc,
    string ProcessName,
    int DurationSeconds,
    IReadOnlyList<FrameSample> Samples,
    FrameStats Stats,
    string Label)
{
    /// <summary>本地时间的创建时刻（UI 展示用）。</summary>
    public DateTime CreatedAtLocal => CreatedAtUtc.ToLocalTime();
}
