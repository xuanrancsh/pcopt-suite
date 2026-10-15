// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>历史记录条目（轻量摘要，不含逐帧数据）。</summary>
/// <param name="SessionId">会话 Id。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="ProcessName">目标进程名。</param>
/// <param name="DurationSeconds">采集时长（秒）。</param>
/// <param name="FrameCount">帧数。</param>
/// <param name="AverageFps">平均 FPS。</param>
/// <param name="LowOnePercentFps">1% Low FPS（百分位法）。</param>
/// <param name="Label">标签。</param>
public sealed record CaptureHistoryEntry(
    string SessionId,
    DateTime CreatedAtUtc,
    string ProcessName,
    int DurationSeconds,
    int FrameCount,
    double AverageFps,
    double LowOnePercentFps,
    string Label)
{
    /// <summary>本地时间的创建时刻（UI 展示用）。</summary>
    public DateTime CreatedAtLocal => CreatedAtUtc.ToLocalTime();
}

/// <summary>
/// 采集历史库（C-P1-3 的轻量实现）：把每次采集的关键指标落盘为 JSON。
/// 读取失败 / 文件损坏一律降级为空列表，绝不抛异常。
/// </summary>
public sealed class HistoryService
{
    private readonly string _filePath;
    private readonly ILogger<HistoryService> _logger;
    private readonly List<CaptureHistoryEntry> _entries = new();

    /// <summary>初始化历史库（默认 <c>%AppData%\pcopt\FrameLab\history.json</c>）。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="filePath">历史文件路径（测试可注入临时路径）。</param>
    public HistoryService(ILogger<HistoryService> logger, string? filePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(ResourcePaths.DataDirectory, "history.json")
            : filePath;
        Load();
    }

    /// <summary>全部历史条目（最近在前）。</summary>
    public IReadOnlyList<CaptureHistoryEntry> Entries => _entries.OrderByDescending(e => e.CreatedAtUtc).ToList();

    /// <summary>追加一条采集记录并落盘。</summary>
    /// <param name="session">采集会话。</param>
    public void Add(CaptureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _entries.Add(new CaptureHistoryEntry(
            session.SessionId,
            session.CreatedAtUtc,
            session.ProcessName,
            session.DurationSeconds,
            session.Stats.FrameCount,
            session.Stats.AverageFps,
            session.Stats.LowOnePercentFps(LowOnePercentAlgorithm.Percentile),
            session.Label));
        Save();
    }

    /// <summary>清空历史。</summary>
    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<CaptureHistoryEntry>>(json);
            if (loaded is not null)
            {
                _entries.AddRange(loaded);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("读取采集历史失败，按空历史处理：{Error}", ex.Message);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("写入采集历史失败：{Error}", ex.Message);
        }
    }
}
