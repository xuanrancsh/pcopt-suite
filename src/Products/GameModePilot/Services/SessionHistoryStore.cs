// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 会话历史存储（T15，架构 3.5 的 %AppData% 用户数据目录）。
/// 位置：<c>%AppData%\pcopt\GameModePilot\sessions.json</c>；原子写（写 .tmp → 整体替换，防半页文件）。
/// 读取失败 / 文件损坏一律降级为空历史（历史仅用于展示，绝不阻断主流程）。
/// </summary>
public sealed class SessionHistoryStore
{
    /// <summary>保留的最大记录数（PRD：最近 10 次展示，多存一些便于排查）。</summary>
    public const int MaxRecords = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rootDirectory;
    private readonly ILogger<SessionHistoryStore> _logger;
    private readonly object _gate = new();

    /// <summary>初始化存储。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="rootDirectory">存储根目录；null 取 %AppData%\pcopt\GameModePilot（单测注入临时目录）。</param>
    public SessionHistoryStore(ILogger<SessionHistoryStore> logger, string? rootDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? ResourcePaths.DataDirectory : rootDirectory;
    }

    /// <summary>存储根目录。</summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>会话文件路径。</summary>
    public string FilePath => Path.Combine(_rootDirectory, "sessions.json");

    /// <summary>读取最近 N 条记录（按开始时间倒序）。</summary>
    public IReadOnlyList<SessionRecord> ListRecent(int count = 10)
    {
        if (count <= 0)
        {
            return Array.Empty<SessionRecord>();
        }

        lock (_gate)
        {
            return LoadAll()
                .OrderByDescending(record => record.StartedAtUtc)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>开启一条会话记录（同 SessionId 覆盖）。</summary>
    public void Begin(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Mutate(records =>
        {
            var index = records.FindIndex(item => string.Equals(item.SessionId, record.SessionId, StringComparison.Ordinal));
            if (index >= 0)
            {
                records[index] = record;
            }
            else
            {
                records.Add(record);
            }
        });
    }

    /// <summary>结束一条会话记录（标记结束时间与还原结果）。</summary>
    public void Complete(string sessionId, int revertedCount, bool fullyReverted)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        Mutate(records =>
        {
            var index = records.FindIndex(item => string.Equals(item.SessionId, sessionId, StringComparison.Ordinal));
            if (index >= 0)
            {
                records[index] = records[index] with
                {
                    EndedAtUtc = DateTime.UtcNow,
                    RevertedCount = revertedCount,
                    FullyReverted = fullyReverted,
                };
            }
        });
    }

    private void Mutate(Action<List<SessionRecord>> mutate)
    {
        lock (_gate)
        {
            var records = LoadAll().ToList();
            mutate(records);

            if (records.Count > MaxRecords)
            {
                records = records
                    .OrderByDescending(record => record.StartedAtUtc)
                    .Take(MaxRecords)
                    .ToList();
            }

            Save(records);
        }
    }

    private List<SessionRecord> LoadAll()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new List<SessionRecord>();
            }

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<SessionRecord>>(json, JsonOptions) ?? new List<SessionRecord>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("会话历史读取失败，按空历史处理");
            return new List<SessionRecord>();
        }
    }

    private void Save(List<SessionRecord> records)
    {
        try
        {
            Directory.CreateDirectory(_rootDirectory);
            var json = JsonSerializer.Serialize(records, JsonOptions);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("会话历史写入失败：{Message}", ex.Message);
        }
    }
}
