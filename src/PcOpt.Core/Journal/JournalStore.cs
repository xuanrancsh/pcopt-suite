// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Native;

namespace PcOpt.Core.Journal;

/// <summary>
/// 还原日志存储（架构 3.2）。
/// 位置：<c>%ProgramData%\pcopt\journal\{sessionId}.json</c>。
/// 原子写：写 <c>.tmp</c> 临时文件并冲刷到磁盘 → <c>MoveFileEx(REPLACE_EXISTING)</c> 替换，
/// 保证任何时刻目标文件要么是旧完整页、要么是新完整页，绝不出现半页文件（PRD A-P0-5）。
/// </summary>
public sealed class JournalStore
{
    private const string ProductDirectoryName = "pcopt";
    private const string JournalDirectoryName = "journal";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _rootDirectory;
    private readonly ILogger<JournalStore> _logger;

    /// <summary>使用默认根目录（%ProgramData%\pcopt\journal）。</summary>
    public JournalStore(ILogger<JournalStore> logger)
        : this(logger, null)
    {
    }

    /// <summary>使用自定义根目录（测试隔离）。</summary>
    public JournalStore(ILogger<JournalStore> logger, string? rootDirectory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? DefaultRootDirectory : rootDirectory;
    }

    /// <summary>默认根目录：%ProgramData%\pcopt\journal。</summary>
    public static string DefaultRootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ProductDirectoryName,
        JournalDirectoryName);

    /// <summary>实际使用的根目录。</summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>开启新会话（立即原子落盘，状态为 <see cref="JournalStatus.Open"/>）。</summary>
    public Result<string> BeginSession(string product)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            return Result<string>.Fail("产品名不能为空");
        }

        try
        {
            var sessionId = Guid.NewGuid().ToString("N");
            var journal = new Journal(sessionId, product, DateTime.UtcNow, JournalStatus.Open);
            var save = Save(journal);
            if (!save.IsSuccess)
            {
                return Result<string>.Fail(save.Error);
            }

            _logger.LogInformation("还原日志会话已开启（SessionId: {SessionId}）", sessionId);
            return Result<string>.Ok(sessionId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"无法创建还原日志：{ex.Message}");
        }
    }

    /// <summary>向会话追加条目（原子写整个会话文件）。</summary>
    public Result AppendEntry(string sessionId, JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return AppendEntries(sessionId, new[] { entry });
    }

    /// <summary>向会话批量追加条目（单次原子写）。</summary>
    public Result AppendEntries(string sessionId, IReadOnlyCollection<JournalEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Result.Fail("会话 Id 不能为空");
        }

        ArgumentNullException.ThrowIfNull(entries);

        var load = Load(sessionId);
        if (!load.IsSuccess)
        {
            return Result.Fail(load.Error);
        }

        var journal = load.Value;
        if (journal is null)
        {
            return Result.Fail($"还原日志会话不存在（SessionId: {sessionId}）");
        }

        journal.Entries.AddRange(entries);
        return Save(journal);
    }

    /// <summary>加载会话；不存在时返回 Ok(null)，文件损坏时返回失败（明确错误）。</summary>
    public Result<Journal?> Load(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Result<Journal?>.Fail("会话 Id 不能为空");
        }

        var path = GetSessionPath(sessionId);
        try
        {
            if (!File.Exists(path))
            {
                return Result<Journal?>.Ok(null);
            }

            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path), JsonOptions);
            return Result<Journal?>.Ok(journal);
        }
        catch (JsonException ex)
        {
            return Result<Journal?>.Fail($"还原日志文件损坏（SessionId: {sessionId}）：{ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Journal?>.Fail($"读取还原日志失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 列出全部会话（按开始时间升序）。损坏文件跳过并记录警告（不阻断其他会话的恢复）。
    /// </summary>
    public IReadOnlyList<Journal> ListSessions()
    {
        try
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return Array.Empty<Journal>();
            }

            var sessions = new List<Journal>();
            foreach (var file in Directory.GetFiles(_rootDirectory, "*.json"))
            {
                try
                {
                    var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(file), JsonOptions);
                    if (journal is not null)
                    {
                        sessions.Add(journal);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("还原日志文件损坏已跳过（文件名: {FileName}）", Path.GetFileName(file));
                }
            }

            return sessions.OrderBy(s => s.StartedAt).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "扫描还原日志目录失败");
            return Array.Empty<Journal>();
        }
    }

    /// <summary>原子保存（写整个会话文件）。</summary>
    public Result Save(Journal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        try
        {
            Directory.CreateDirectory(_rootDirectory);
            var json = JsonSerializer.Serialize(journal, JsonOptions);
            AtomicWriteFile(GetSessionPath(journal.SessionId), json);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return Result.Fail($"写入还原日志失败：{ex.Message}");
        }
    }

    /// <summary>会话文件路径。</summary>
    public string GetSessionPath(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("会话 Id 不能为空", nameof(sessionId));
        }

        // 防御路径穿越：sessionId 仅允许 Guid 十六进制字符
        if (sessionId.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ArgumentException("会话 Id 含有非法字符", nameof(sessionId));
        }

        return Path.Combine(_rootDirectory, sessionId + ".json");
    }

    /// <summary>
    /// 原子写：唯一临时名 → 写入并冲刷到磁盘 → MoveFileEx(REPLACE_EXISTING) 替换；
    /// 任一步骤失败都清理临时文件，不在磁盘留下半页文件。
    /// </summary>
    private void AtomicWriteFile(string targetPath, string content)
    {
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true); // 冲刷至磁盘，防断电半页文件
            }

            if (OperatingSystem.IsWindows())
            {
                if (!NativeMethods.MoveFileEx(tempPath, targetPath, NativeMethods.MoveFileReplaceExisting))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "替换还原日志文件失败");
                }
            }
            else
            {
                File.Move(tempPath, targetPath, overwrite: true);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响主流程（临时文件以 .tmp 结尾，ListSessions 不会扫描）
        }
    }
}
