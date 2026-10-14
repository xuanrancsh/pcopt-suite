// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace PcOpt.Core.Game;

/// <summary>
/// 游戏进程监视器（T8：ETW 实时进程创建监控，架构 3.4）。
/// 设计说明：
/// <list type="bullet">
/// <item>内核进程事件源：TraceEvent <see cref="KernelTraceEventParser.Keywords.Process"/> 即架构 3.4 所述
/// Microsoft-Windows-Kernel-Process provider 的进程创建通道（ProcessStart 事件，含可执行路径 / 进程 Id）；</item>
/// <item>≤3 秒触发：内核 ETW 事件为直投（微秒级派发），匹配为 O(1) 字典查找，
/// 端到端延迟远低于 3 秒——真机端到端验证留待 T14（需管理员权限的真实环境）；</item>
/// <item>权限：实时内核 ETW 会话需要管理员权限（或 SeSystemProfilePrivilege），
/// 无权时 <see cref="Start"/> 返回明确失败而非抛异常（架构 8.2）；</item>
/// <item>合规：仅消费进程创建事件，不打开目标进程、不读写内存（红线 #2 / #4）。</item>
/// </list>
/// </summary>
public sealed class GameProcessWatcher : IDisposable
{
    /// <summary>ETW 会话名（固定，便于与系统其他会话区分）。</summary>
    public const string SessionName = "pcopt-game-watcher";

    private readonly IProcessMatcher _matcher;
    private readonly ILogger<GameProcessWatcher> _logger;
    private readonly object _gate = new();
    private TraceEventSession? _session;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>初始化监视器。</summary>
    /// <param name="matcher">进程匹配器（可注入替身）。</param>
    /// <param name="logger">日志。</param>
    public GameProcessWatcher(IProcessMatcher matcher, ILogger<GameProcessWatcher> logger)
    {
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>游戏检测事件（在 ETW 处理线程触发；订阅方应自行切到 UI 线程）。</summary>
    public event Action<GameMatchResult>? GameDetected;

    /// <summary>监视器是否正在运行。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _processingTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// 启动 ETW 会话。失败场景（均已确认不会抛到调用方）：
    /// 权限不足 / 同名会话已存在 / 会话创建被系统拒绝。
    /// </summary>
    public Result Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Result.Fail("监视器已释放，无法启动");
            }

            if (IsRunning)
            {
                return Result.Fail("监视器已在运行");
            }

            try
            {
                var session = new TraceEventSession(SessionName);
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

                _session = session;
                _cts = new CancellationTokenSource();
                _processingTask = Task.Run(() => ProcessEvents(_cts.Token), _cts.Token);
                return Result.Ok();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // 典型：非管理员（拒绝访问）/ 会话已存在 / 系统限制
                _logger.LogWarning(ex, "启动 ETW 会话失败");
                CleanupSession();
                return Result.Fail($"无法启动进程监控（需要管理员权限）：{ex.Message}");
            }
        }
    }

    /// <summary>停止 ETW 会话并等待处理线程退出。</summary>
    public async Task StopAsync()
    {
        Task? processingTask;
        lock (_gate)
        {
            if (_cts is null || _processingTask is null)
            {
                return;
            }

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已取消：忽略
            }

            processingTask = _processingTask;
        }

        CleanupSession();

        if (processingTask is not null)
        {
            try
            {
                await processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期：取消即停止
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ETW 处理线程退出异常");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // 同步等待处理线程退出（Dispose 不能异步）；超时后放弃，避免死锁
        Task? processingTask;
        lock (_gate)
        {
            processingTask = _processingTask;
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已释放：忽略
            }
        }

        CleanupSession();

        try
        {
            if (processingTask is not null)
            {
                _ = processingTask.Wait(TimeSpan.FromSeconds(5));
            }
        }
        catch (AggregateException)
        {
            // 处理线程异常已在其内部记录；这里只保证不抛到 Dispose
        }

        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ProcessEvents(CancellationToken cancellationToken)
    {
        try
        {
            using var source = new ETWTraceEventSource(SessionName, TraceEventSourceType.Session);
            source.Kernel.ProcessStart += OnProcessStart;
            _logger.LogInformation("进程监控已启动（会话: {Session}）", SessionName);

            // 阻塞循环：事件到达即回调；会话停止时返回
            source.Process();
        }
        catch (OperationCanceledException)
        {
            // 取消：正常退出路径
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW 事件处理异常，监控停止");
        }
    }

    private void OnProcessStart(ProcessTraceData data)
    {
        if (_cts?.IsCancellationRequested != false)
        {
            return;
        }

        try
        {
            var executablePath = data.ImageFileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            var info = new ProcessEventInfo(data.ProcessID, executablePath, data.CommandLine);
            var match = _matcher.Match(info);
            if (match is null)
            {
                return;
            }

            _logger.LogInformation("检测到游戏进程：{Game}（{Executable}）", match.Game.Name, executablePath);
            GameDetected?.Invoke(match);
        }
        catch (Exception ex)
        {
            // 单个事件处理异常绝不中断 ETW 循环（零误操作原则）
            _logger.LogDebug(ex, "处理进程创建事件失败");
        }
    }

    private void CleanupSession()
    {
        TraceEventSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        try
        {
            session?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放 ETW 会话异常");
        }
    }
}
