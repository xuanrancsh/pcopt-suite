// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Game;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 进程匹配器：数据库缺失时的空实现（零误触发；守护仍可通过手动/外部触发工作）。
/// </summary>
public sealed class NullProcessMatcher : IProcessMatcher
{
    /// <inheritdoc/>
    public GameMatchResult? Match(ProcessEventInfo info) => null;
}

/// <summary>
/// 游戏监视器（T14，架构 3.4 游戏会话时序）。
/// 组成：内核 <see cref="GameProcessWatcher"/>（ETW 进程创建）+ <see cref="GameLibraryScanner"/>（库扫描）。
/// 增强：内核监视器只报告"进程创建"，本类在命中后跟踪该进程直到退出并触发
/// <see cref="GameExited"/>，构成完整的"游戏启动 → 游戏退出"闭环。
/// 合规：仅消费进程事件与库清单，不打开 / 不读写游戏进程（红线 #2 / #4）。
/// </summary>
public sealed class GameWatcher : IDisposable
{
    private readonly GameProcessWatcher _watcher;
    private readonly GameLibraryScanner _scanner;
    private readonly ILogger<GameWatcher> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<int> _trackedProcessIds = new();
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>初始化监视器。</summary>
    public GameWatcher(
        GameProcessWatcher watcher,
        GameLibraryScanner scanner,
        ILogger<GameWatcher> logger)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _watcher.GameDetected += OnGameDetected;
    }

    /// <summary>检测到游戏启动（ETW 线程触发；订阅方需自行切到 UI 线程）。</summary>
    public event EventHandler<GameMatchResult>? GameStarted;

    /// <summary>检测到游戏退出。</summary>
    public event EventHandler<GameMatchResult>? GameExited;

    /// <summary>ETW 会话是否运行中。</summary>
    public bool IsRunning => _watcher.IsRunning;

    /// <summary>启动 ETW 监控（失败返回明确原因，如权限不足）。</summary>
    public Result Start()
    {
        var result = _watcher.Start();
        if (!result.IsSuccess)
        {
            _logger.LogWarning("游戏进程监控未能启动：{Error}", result.Error);
        }

        return result;
    }

    /// <summary>停止 ETW 监控。</summary>
    public Task StopAsync() => _watcher.StopAsync();

    /// <summary>扫描各平台游戏库（Steam / Epic / GOG / Xbox / WeGame）。</summary>
    public Task<Result<LibraryScanReport>> ScanLibraryAsync(CancellationToken cancellationToken = default) =>
        _scanner.ScanAsync(cancellationToken);

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

        _watcher.GameDetected -= OnGameDetected;

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已释放：忽略
        }

        _watcher.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnGameDetected(GameMatchResult match)
    {
        lock (_gate)
        {
            if (_disposed || !_trackedProcessIds.Add(match.ProcessId))
            {
                return;
            }
        }

        _logger.LogInformation("检测到游戏启动：{Game}", match.Game.Name);
        GameStarted?.Invoke(this, match);
        _ = TrackExitAsync(match);
    }

    private async Task TrackExitAsync(GameMatchResult match)
    {
        try
        {
            using var process = Process.GetProcessById(match.ProcessId);
            await process.WaitForExitAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            // 进程已退出 / 不可访问 / 取消：统一按"退出"处理
            _logger.LogDebug(ex, "跟踪游戏进程退出结束（进程 Id: {ProcessId}）", match.ProcessId);
        }

        lock (_gate)
        {
            _trackedProcessIds.Remove(match.ProcessId);
            if (_disposed)
            {
                return;
            }
        }

        _logger.LogInformation("检测到游戏退出：{Game}", match.Game.Name);
        GameExited?.Invoke(this, match);
    }
}
