// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Game;
using PcOpt.Products.GameModePilot.Services;
using PcOpt.UI.Controls;

namespace PcOpt.Products.GameModePilot.ViewModels;

/// <summary>
/// 主界面视图模型（T15，PRD 2.6 线框）：大状态卡（三态）/ 调整项网格 / 最近会话 / 全部还原。
/// 订阅 <see cref="SessionEngine.StateChanged"/> 把会话状态实时映射到三态卡（状态变化 ≤ 1 秒）。
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SessionEngine _session;
    private readonly GuardianService _guardian;
    private readonly SessionHistoryStore _history;
    private readonly TweakPreferences _preferences;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    /// <summary>初始化主界面视图模型。</summary>
    public MainViewModel(
        SessionEngine session,
        GuardianService guardian,
        SessionHistoryStore history,
        TweakPreferences preferences,
        ILogger<MainViewModel> logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _guardian = guardian ?? throw new ArgumentNullException(nameof(guardian));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _session.StateChanged += OnSessionStateChanged;
        _guardian.SessionEnded += OnSessionEnded;

        RebuildTweaks();
        ApplySnapshot(_session.Current);
        RefreshHistory();
    }

    /// <summary>请求弹出通知（由视图承接 ToastHost）。</summary>
    public event EventHandler<string>? ToastRequested;

    /// <summary>调整项网格。</summary>
    public ObservableCollection<TweakItemViewModel> Tweaks { get; } = new();

    /// <summary>最近会话（最多 10 条）。</summary>
    public ObservableCollection<SessionHistoryItemViewModel> RecentSessions { get; } = new();

    /// <summary>大状态卡三态。</summary>
    [ObservableProperty]
    private HeroStatus _heroStatus = HeroStatus.Guarding;

    /// <summary>当前游戏名。</summary>
    [ObservableProperty]
    private string _heroGameName = string.Empty;

    /// <summary>已应用项数。</summary>
    [ObservableProperty]
    private int _heroAppliedCount;

    /// <summary>已还原项数。</summary>
    [ObservableProperty]
    private int _heroRevertedCount;

    /// <summary>反作弊环境文案。</summary>
    [ObservableProperty]
    private string _antiCheatText = "反作弊环境：未检测到已知反作弊";

    /// <summary>守护开关（两向绑定）。</summary>
    [ObservableProperty]
    private bool _guardianEnabled = true;

    /// <summary>状态摘要（守护天数 / 今日优化次数）。</summary>
    [ObservableProperty]
    private string _statusSummary = "守护中 · 今日自动优化 0 次";

    /// <summary>全部还原（PRD 5.5：需二次确认）。</summary>
    [RelayCommand]
    private void RevertAll()
    {
        var result = _session.RevertAll();
        if (!result.IsSuccess)
        {
            RaiseToast($"还原未完成：{result.Error}");
            return;
        }

        var value = result.Value!;
        RaiseToast(value.FullyReverted
            ? $"已全部还原，共 {value.RevertedCount} 项，你的电脑恢复正常了"
            : "部分调整项未能还原，已保留还原日志，可在下次启动自动重试");
        RefreshHistory();
    }

    /// <summary>暂停守护指定时长（托盘"暂停守护 1 小时"），到期自动恢复；不改变总开关。</summary>
    /// <param name="duration">暂停时长；≤ 0 表示暂停到显式恢复。</param>
    public void PauseGuardianFor(TimeSpan duration)
    {
        _guardian.PauseFor(duration);
        RaiseToast(duration > TimeSpan.Zero
            ? $"已暂停守护 {(int)duration.TotalHours} 小时，到期后会自动恢复"
            : "已暂停守护，重新打开主界面可恢复");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.StateChanged -= OnSessionStateChanged;
        _guardian.SessionEnded -= OnSessionEnded;
        GC.SuppressFinalize(this);
    }

    /// <summary>守护开关变化时同步到守护服务。</summary>
    partial void OnGuardianEnabledChanged(bool value)
    {
        _ = value ? _guardian.EnableGuardianAsync(null) : _guardian.DisableGuardianAsync();
        UpdateStatusSummary();
    }

    private void RebuildTweaks()
    {
        Tweaks.Clear();
        foreach (var rule in RuleSetBuilder.DescribeAll())
        {
            Tweaks.Add(new TweakItemViewModel(
                rule.Id,
                rule.DisplayName,
                rule.Description,
                rule.Risk,
                _preferences.IsEnabled(rule.Id),
                ApplyTweakAsync,
                RevertTweakAsync));
        }
    }

    private Task ApplyTweakAsync(TweakItemViewModel item)
    {
        _preferences.SetEnabled(item.RuleId, true);
        item.IsEnabled = true;
        RaiseToast($"已启用：{item.Title}");
        return Task.CompletedTask;
    }

    private Task RevertTweakAsync(TweakItemViewModel item)
    {
        _preferences.SetEnabled(item.RuleId, false);
        item.IsEnabled = false;
        RaiseToast($"已关闭：{item.Title}，之后的游戏会话不再应用这一项");
        return Task.CompletedTask;
    }

    private void OnSessionStateChanged(object? sender, SessionSnapshot snapshot) =>
        _dispatcher.Invoke(() => ApplySnapshot(snapshot));

    private void OnSessionEnded(object? sender, SessionEndResult result) =>
        _dispatcher.Invoke(() =>
        {
            RaiseToast(result.FullyReverted
                ? $"游戏已退出，已全部还原，共 {result.RevertedCount} 项"
                : "游戏已退出，但有调整项未能还原，已保留还原日志");
            RefreshHistory();
        });

    private void ApplySnapshot(SessionSnapshot snapshot)
    {
        HeroStatus = snapshot.Phase switch
        {
            SessionPhase.Guarding => HeroStatus.Guarding,
            SessionPhase.Starting or SessionPhase.Playing => HeroStatus.Playing,
            _ => HeroStatus.Restored,
        };

        HeroGameName = snapshot.GameName ?? string.Empty;
        HeroAppliedCount = snapshot.AppliedCount;
        HeroRevertedCount = snapshot.RevertedCount;
        AntiCheatText = snapshot.AntiCheat switch
        {
            AntiCheatProfile.Kernel => "反作弊环境：检测到内核级反作弊，进程调度已自动切换为兼容方案",
            AntiCheatProfile.UserMode => "反作弊环境：检测到用户态反作弊",
            _ => "反作弊环境：未检测到已知反作弊",
        };

        UpdateStatusSummary();
    }

    private void RefreshHistory()
    {
        RecentSessions.Clear();
        foreach (var record in _history.ListRecent(10))
        {
            var started = record.StartedAtUtc.ToLocalTime();
            var duration = record.EndedAtUtc is { } ended
                ? ended - record.StartedAtUtc
                : DateTime.UtcNow - record.StartedAtUtc;
            var status = record.FullyReverted ? "已全部还原" : "进行中";
            var detail = $"{started:MM-dd HH:mm} · {FormatDuration(duration)} · 应用 {record.AppliedCount} 项 · {status}";
            RecentSessions.Add(new SessionHistoryItemViewModel($"🎮 {record.GameName}", detail));
        }

        UpdateStatusSummary();
    }

    private void UpdateStatusSummary()
    {
        var today = DateTime.Now.Date;
        var todayCount = _history.ListRecent(SessionHistoryStore.MaxRecords)
            .Count(record => record.StartedAtUtc.ToLocalTime().Date == today);

        if (!GuardianEnabled)
        {
            StatusSummary = $"已暂停守护 · 今日自动优化 {todayCount} 次";
            return;
        }

        StatusSummary = $"守护中 · 今日自动优化 {todayCount} 次";
    }

    private void RaiseToast(string message)
    {
        if (_dispatcher.CheckAccess())
        {
            ToastRequested?.Invoke(this, message);
        }
        else
        {
            _dispatcher.Invoke(() => ToastRequested?.Invoke(this, message));
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h{duration.Minutes:D2}m"
            : $"{duration.Minutes}m";
    }
}
