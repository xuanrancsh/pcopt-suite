// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Config;
using PcOpt.Core.Game;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 守护服务（T14 / T17 / T18 粘合层）：持有"守护开关 / 暂停"状态，
/// 订阅 <see cref="GameWatcher"/> 的启动 / 退出事件驱动 <see cref="SessionEngine"/>，
/// 并实现 <see cref="IIpcCommandHandler"/> 供产品 B/C 调用（架构 4.1）。
/// 状态语义（与 IPC 契约对齐）：
/// <list type="bullet">
/// <item><c>enable</c>（无 minutes）：启用守护，直到显式暂停；</item>
/// <item><c>enable</c>（带 minutes）：启用守护并设置保持时长，到期自动转暂停；</item>
/// <item><c>disable</c>：暂停守护（<see cref="PauseFor"/> 支持定时恢复，用于托盘"暂停 1 小时"）。</item>
/// </list>
/// </summary>
public sealed class GuardianService : IIpcCommandHandler, IDisposable
{
    private readonly GameWatcher _watcher;
    private readonly SessionEngine _session;
    private readonly TweakPreferences _preferences;
    private readonly ILogger<GuardianService> _logger;
    private readonly object _gate = new();

    private bool _enabled = true;
    private DateTime? _enabledUntilUtc;
    private DateTime? _pausedUntilUtc;
    private bool _disposed;

    /// <summary>初始化守护服务。</summary>
    public GuardianService(GameWatcher watcher, SessionEngine session, TweakPreferences preferences, ILogger<GuardianService> logger)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _watcher.GameStarted += OnGameStarted;
        _watcher.GameExited += OnGameExited;
    }

    /// <summary>会话结束（还原完成）事件，供 UI 弹 Toast。</summary>
    public event EventHandler<SessionEndResult>? SessionEnded;

    /// <summary>守护是否启用（含暂停判定）。</summary>
    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    /// <summary>当前守护是否处于生效状态（启用且未暂停 / 未过期）。</summary>
    public bool ShouldGuard
    {
        get
        {
            lock (_gate)
            {
                return EvaluateGuardLocked();
            }
        }
    }

    /// <summary>启动监控与守护。</summary>
    public Result Start()
    {
        var result = _watcher.Start();
        if (!result.IsSuccess)
        {
            _logger.LogWarning("守护启动时未能开启进程监控：{Error}", result.Error);
        }

        return result;
    }

    /// <summary>暂停守护指定时长（到期自动恢复）；时长 ≤ 0 表示暂停到显式恢复。</summary>
    public void PauseFor(TimeSpan duration)
    {
        lock (_gate)
        {
            // 契约：duration > 0 → 暂停到该时刻自动恢复；duration ≤ 0 → 暂停直到显式恢复
            //（用 DateTime.MaxValue 表示"无到期时间"，EvaluateGuardLocked 的 now>=pausedUntil 永不成立）。
            _pausedUntilUtc = duration > TimeSpan.Zero ? DateTime.UtcNow.Add(duration) : DateTime.MaxValue;
        }
    }

    /// <inheritdoc/>
    public Task<IpcStatus> GetStatusAsync()
    {
        SessionSnapshot snapshot = _session.Current;
        bool enabled;
        lock (_gate)
        {
            enabled = EvaluateGuardLocked();
        }

        return Task.FromResult(new IpcStatus(
            enabled,
            snapshot.Phase.ToString(),
            snapshot.GameName,
            snapshot.AppliedCount,
            snapshot.AntiCheat.ToString()));
    }

    /// <inheritdoc/>
    public Task EnableGuardianAsync(int? minutes)
    {
        lock (_gate)
        {
            _enabled = true;
            _pausedUntilUtc = null;
            _enabledUntilUtc = minutes is int value && value > 0 ? DateTime.UtcNow.AddMinutes(value) : null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisableGuardianAsync()
    {
        lock (_gate)
        {
            _enabled = false;
            _enabledUntilUtc = null;
            _pausedUntilUtc = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Result> ApplyProfileAsync(IReadOnlyList<string> ruleIds)
    {
        var result = _session.ApplyProfile(ruleIds);
        return Task.FromResult(result.IsSuccess ? Result.Ok() : Result.Fail(result.Error));
    }

    /// <inheritdoc/>
    public Task<Result<int>> RevertAllAsync()
    {
        var result = _session.RevertAll();
        return Task.FromResult(result.IsSuccess
            ? Result<int>.Ok(result.Value!.RevertedCount)
            : Result<int>.Fail(result.Error));
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

        _watcher.GameStarted -= OnGameStarted;
        _watcher.GameExited -= OnGameExited;
        GC.SuppressFinalize(this);
    }

    private void OnGameStarted(object? sender, GameMatchResult match)
    {
        if (!ShouldGuard)
        {
            _logger.LogInformation("守护已暂停，跳过游戏 {Game}", match.Game.Name);
            return;
        }

        var request = new SessionRequest(
            match.Game.Id,
            match.Game.Name,
            match.Game.Executables,
            ProfileIntensity.Balanced,
            _preferences.DisabledRuleIds);

        var result = _session.BeginSession(request);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("开始游戏会话失败：{Error}", result.Error);
        }
    }

    private void OnGameExited(object? sender, GameMatchResult match)
    {
        if (!_session.IsPlaying)
        {
            return;
        }

        var result = _session.EndSession();
        if (result.IsSuccess)
        {
            SessionEnded?.Invoke(this, result.Value!);
        }
        else
        {
            _logger.LogWarning("结束游戏会话失败：{Error}", result.Error);
        }
    }

    /// <summary>在锁内计算守护是否生效（并顺带清理过期状态）。</summary>
    private bool EvaluateGuardLocked()
    {
        var now = DateTime.UtcNow;

        if (_pausedUntilUtc is { } pausedUntil && now >= pausedUntil)
        {
            _pausedUntilUtc = null;
        }

        if (_enabledUntilUtc is { } enabledUntil && now >= enabledUntil)
        {
            _enabled = false;
            _enabledUntilUtc = null;
        }

        if (!_enabled)
        {
            return false;
        }

        return _pausedUntilUtc is null;
    }
}
