// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 简单文件日志提供程序（架构 8.3）：写入 <c>%LocalAppData%\pcopt\FrameLab\logs\app.log</c>。
/// 合规：日志<b>绝不</b>包含用户数据、硬件序列号或含用户名的路径（由调用方保证消息内容）。
/// 写入失败静默降级（日志自身不得影响主流程）。
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly LogLevel _minimumLevel;
    private readonly object _gate = new();
    private readonly bool _available;

    /// <summary>初始化文件日志提供程序。</summary>
    /// <param name="filePath">日志文件路径。</param>
    /// <param name="minimumLevel">最低记录级别。</param>
    public FileLoggerProvider(string filePath, LogLevel minimumLevel = LogLevel.Information)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _minimumLevel = minimumLevel;

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _available = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _available = false;
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
        // 无长期持有的句柄（每次追加即关闭），无需释放
    }

    private void Append(string line)
    {
        if (!_available)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写入失败不影响主流程
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logLevel >= _provider._minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            _provider.Append(line);
        }
    }
}
