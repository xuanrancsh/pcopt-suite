// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>产品 C 用户设置（JSON 持久化）。</summary>
public sealed class FrameLabSettings
{
    /// <summary>默认采集时长（秒）。</summary>
    public int DurationSeconds { get; set; } = 60;

    /// <summary>上次使用的目标进程名。</summary>
    public string LastProcessName { get; set; } = string.Empty;

    /// <summary>报告中默认展示的 1% Low 口径。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LowOnePercentAlgorithm PreferredAlgorithm { get; set; } = LowOnePercentAlgorithm.Percentile;

    /// <summary>是否在报告中展示温度 / 功耗曲线。</summary>
    public bool ShowThermal { get; set; } = true;
}

/// <summary>
/// 设置服务（JSON 持久化到 <c>%AppData%\pcopt\FrameLab\settings.json</c>）。
/// 读取失败 / 损坏一律回落到默认值，绝不抛异常。
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>初始化设置服务。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="filePath">设置文件路径（测试可注入临时路径）。</param>
    public SettingsService(ILogger<SettingsService> logger, string? filePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(ResourcePaths.DataDirectory, "settings.json")
            : filePath;
        Current = Load();
    }

    /// <summary>当前设置（修改后调用 <see cref="Save"/> 落盘）。</summary>
    public FrameLabSettings Current { get; private set; }

    /// <summary>持久化当前设置。</summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(Current, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("保存设置失败：{Error}", ex.Message);
        }
    }

    private FrameLabSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new FrameLabSettings();
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<FrameLabSettings>(json, Options) ?? new FrameLabSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("读取设置失败，使用默认设置：{Error}", ex.Message);
            return new FrameLabSettings();
        }
    }
}
