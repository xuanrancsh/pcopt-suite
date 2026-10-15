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

    /// <summary>瞬时 I/O 错误的最大尝试次数（首次 + 有界重试）。</summary>
    private const int MaxIoAttempts = 5;

    /// <summary>重试退避（毫秒）：覆盖杀软实时扫描 / 原子替换瞬间的共享冲突窗口。</summary>
    private static readonly int[] IoBackoffMilliseconds = { 10, 20, 40, 80 };

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

            var journal = RetryIo(() => JsonSerializer.Deserialize<Journal>(File.ReadAllText(path), JsonOptions));
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
    /// 宽松列出全部会话（按开始时间升序）。
    /// <para>仅用于 UI 展示等<b>非关键</b>路径：损坏（<see cref="JsonException"/>）文件跳过并告警；
    /// 读取过程中出现瞬时 I/O 错误（含共享冲突）时先做有界重试，重试仍失败的文件跳过并告警，
    /// 不影响其余会话。</para>
    /// <para>⚠️ 还原 / 崩溃恢复 / 闭合 Open 条目等<b>关键路径</b>必须使用 <see cref="TryListSessions"/>：
    /// 绝不可把"读不到"当成"没有待还原项"（内核"崩溃安全 + 可还原"承诺的基石）。</para>
    /// </summary>
    public IReadOnlyList<Journal> ListSessions()
    {
        var sessions = ScanSessions(strict: false, out _);
        return sessions;
    }

    /// <summary>
    /// 严格列出全部会话（按开始时间升序）。
    /// <para>损坏（<see cref="JsonException"/>）文件跳过并告警（无法解析的文件不可能含有可用信息，
    /// 且不阻断其他会话的恢复）；但任何 <b>I/O 错误</b>（有界重试后仍失败，如共享冲突 / 拒绝访问 /
    /// 杀软扫描占用）都会让本方法<b>显式失败</b>，绝不静默返回"空会话"。</para>
    /// 用于还原 / 崩溃恢复 / 闭合 Open 条目等关键路径：宁可失败并明确报错，也绝不静默地"零操作成功"。
    /// </summary>
    public Result<IReadOnlyList<Journal>> TryListSessions()
    {
        var sessions = ScanSessions(strict: true, out var error);
        return error is null
            ? Result<IReadOnlyList<Journal>>.Ok(sessions)
            : Result<IReadOnlyList<Journal>>.Fail(error);
    }

    /// <summary>
    /// 扫描会话文件（关键路径与展示路径共用）。
    /// </summary>
    /// <param name="strict">
    /// true：I/O 错误上抛为失败（关键路径）；false：I/O 错误跳过并告警（展示路径）。
    /// </param>
    /// <param name="error">strict 模式下 I/O 失败的中文原因；成功或宽松模式为 null。</param>
    private IReadOnlyList<Journal> ScanSessions(bool strict, out string? error)
    {
        error = null;
        if (!Directory.Exists(_rootDirectory))
        {
            return Array.Empty<Journal>();
        }

        string[] files;
        try
        {
            files = RetryIo(() => Directory.GetFiles(_rootDirectory, "*.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "扫描还原日志目录失败（{Dir}）", _rootDirectory);
            if (strict)
            {
                error = $"无法读取还原日志目录：{ex.Message}";
            }

            return Array.Empty<Journal>();
        }

        var sessions = new List<Journal>(files.Length);
        foreach (var file in files)
        {
            try
            {
                var text = RetryIo(() => File.ReadAllText(file));
                var journal = JsonSerializer.Deserialize<Journal>(text, JsonOptions);
                if (journal is not null)
                {
                    sessions.Add(journal);
                }
            }
            catch (JsonException)
            {
                // 损坏文件：无法解析，跳过并告警（不阻断其他会话的恢复）
                _logger.LogWarning("还原日志文件损坏已跳过（文件名: {FileName}）", Path.GetFileName(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                // 瞬时 I/O 错误已在 RetryIo 内做有界重试；此处为最终失败。
                // 关键路径（strict）必须显式失败——绝不把"读不到"当成"没有待还原项"。
                _logger.LogWarning(ex, "读取还原日志文件失败（文件名: {FileName}）", Path.GetFileName(file));
                if (strict)
                {
                    error = $"读取还原日志失败（{Path.GetFileName(file)}）：{ex.Message}";
                    return Array.Empty<Journal>();
                }
            }
        }

        return sessions.OrderBy(s => s.StartedAt).ToList();
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
    /// 对瞬时 I/O 错误（原子替换瞬间与杀软扫描 / 并发读的共享冲突）做有界重试 + 退避。
    /// </summary>
    private void AtomicWriteFile(string targetPath, string content)
    {
        RetryIo(() => AtomicWriteFileCore(targetPath, content));
    }

    /// <summary>原子写的单次实现（无重试）；由 <see cref="AtomicWriteFile"/> 负责有界重试。</summary>
    private void AtomicWriteFileCore(string targetPath, string content)
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

    /// <summary>
    /// 有界重试瞬时 I/O 错误：仅在 <see cref="IsTransientIo"/> 为真且未达 <see cref="MaxIoAttempts"/> 时重试，
    /// 之后退避；其余异常原样抛出。
    /// </summary>
    private static T RetryIo<T>(Func<T> operation)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (IsTransientIo(ex) && attempt < MaxIoAttempts - 1)
            {
                Thread.Sleep(IoBackoffMilliseconds[attempt]);
                attempt++;
            }
        }
    }

    /// <summary>无返回值的 <see cref="RetryIo{T}"/>。</summary>
    private static void RetryIo(Action operation) =>
        RetryIo(() =>
        {
            operation();
            return true;
        });

    /// <summary>
    /// 是否属于"值得重试"的瞬时 I/O 错误：
    /// 拒绝访问（Win32 ERROR_ACCESS_DENIED=5）/ 共享冲突（ERROR_SHARING_VIOLATION=32）/
    /// 锁冲突（ERROR_LOCK_VIOLATION=33），以及一般 <see cref="IOException"/>（不含"文件不存在"这类确定性错误）。
    /// </summary>
    private static bool IsTransientIo(Exception ex) =>
        ex is UnauthorizedAccessException
        || (ex is IOException io && io is not FileNotFoundException and not DirectoryNotFoundException)
        || (ex is Win32Exception w32 && w32.NativeErrorCode is 5 or 32 or 33);

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
