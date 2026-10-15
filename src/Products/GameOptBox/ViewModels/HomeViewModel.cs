// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 首页视图模型（T20，PRD 3.6 线框）：电脑健康分（总分 + 三个分项 + 改进建议）、
/// 与产品 A 的"开启自动守护"入口（管道探测，缺失则隐藏）、与产品 C 的"去测一测"入口。
/// </summary>
public sealed partial class HomeViewModel : ObservableObject, IRefreshable
{
    private readonly HealthScoreService _health;
    private readonly RuleRepository _rules;
    private readonly StartupItemService _startup;
    private readonly ServiceCatalogService _services;
    private readonly IpcProbeService _ipc;
    private readonly LauncherService _launcher;
    private readonly INotificationService _notifications;

    /// <summary>初始化首页视图模型。</summary>
    public HomeViewModel(
        HealthScoreService health,
        RuleRepository rules,
        StartupItemService startup,
        ServiceCatalogService services,
        IpcProbeService ipc,
        LauncherService launcher,
        INotificationService notifications)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>健康分总分（0~100）。</summary>
    [ObservableProperty]
    private int _overall;

    /// <summary>开机速度分项。</summary>
    [ObservableProperty]
    private int _startupScore;

    /// <summary>后台服务分项。</summary>
    [ObservableProperty]
    private int _backgroundScore;

    /// <summary>游戏就绪分项。</summary>
    [ObservableProperty]
    private int _readinessScore;

    /// <summary>评级文案。</summary>
    [ObservableProperty]
    private string _grade = "良好";

    /// <summary>改进建议（≤3 条）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _suggestions = Array.Empty<string>();

    /// <summary>建议摘要（按钮副文案）。</summary>
    [ObservableProperty]
    private string _suggestionSummary = "正在体检…";

    /// <summary>产品 A 是否在运行（决定"开启自动守护"入口显隐，架构 4.2）。</summary>
    [ObservableProperty]
    private bool _guardianAvailable;

    /// <summary>产品 C 是否已安装（决定"去测一测"入口显隐）。</summary>
    [ObservableProperty]
    private bool _frameLabAvailable;

    /// <summary>可一键修复的建议项数量。</summary>
    [ObservableProperty]
    private int _fixableCount;

    /// <inheritdoc/>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>重新体检（后台线程采集，UI 线程更新）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var snapshot = await Task.Run(Gather).ConfigureAwait(true);

        Overall = snapshot.Total;
        StartupScore = snapshot.StartupScore;
        BackgroundScore = snapshot.BackgroundScore;
        ReadinessScore = snapshot.ReadinessScore;
        Grade = snapshot.Grade;
        Suggestions = snapshot.Suggestions;
        SuggestionSummary = snapshot.Suggestions.Count > 0
            ? $"一键体检修复（{snapshot.Suggestions.Count} 项建议）"
            : "状态良好，暂无建议";
        FixableCount = snapshot.Suggestions.Count;

        GuardianAvailable = await Task.Run(() => _ipc.IsRunning()).ConfigureAwait(true);
        FrameLabAvailable = await Task.Run(() =>
            ResourcePaths.FindSiblingExecutable(LauncherService.FrameLabExecutable) is not null).ConfigureAwait(true);
    }

    /// <summary>一键体检修复：应用前若干条安全 / 注意级游戏与延迟优化项。</summary>
    [RelayCommand]
    private async Task OneClickFixAsync()
    {
        var candidates = _rules.Rules
            .Where(rule => rule.Risk != RiskLevel.Danger)
            .Where(rule => rule.Category is "游戏" or "延迟" or "性能")
            .Where(rule => !_rules.IsApplied(rule.Id))
            .Take(3)
            .ToList();

        if (candidates.Count == 0)
        {
            _notifications.Notify("没有需要修复的项目，电脑状态已经不错");
            return;
        }

        var applied = 0;
        foreach (var rule in candidates)
        {
            var result = await Task.Run(() => _rules.Apply(rule.Id)).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                applied++;
            }
        }

        _notifications.Notify(applied > 0
            ? $"已应用 {applied} 项优化，可随时在“还原中心”还原"
            : "本次没有可应用的优化项");
        Refresh();
    }

    /// <summary>开启自动守护（探测并请求产品 A）。</summary>
    [RelayCommand]
    private async Task OpenGuardianAsync()
    {
        var result = await Task.Run(() => _ipc.EnableGuardian(60)).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    /// <summary>去测一测（拉起产品 C）。</summary>
    [RelayCommand]
    private async Task OpenFrameLabAsync()
    {
        var result = await Task.Run(() => _launcher.Launch(LauncherService.FrameLabExecutable)).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    private HealthSnapshot Gather()
    {
        var startupCount = 0;
        var startupResult = _startup.Scan();
        if (startupResult.IsSuccess)
        {
            startupCount = startupResult.Value!.Count(item => item.Enabled);
        }

        var nonEssential = 0;
        var servicesResult = _services.Enumerate();
        if (servicesResult.IsSuccess)
        {
            nonEssential = servicesResult.Value!
                .Count(entry => entry.NonEssential && entry.CurrentStart == ServiceStartMode.Automatic);
        }

        var gameRules = _rules.Rules.Where(rule => rule.Category == "游戏").ToList();
        var appliedGame = gameRules.Count(rule => _rules.IsApplied(rule.Id));

        var report = _health.Compute(new HealthSignals(startupCount, nonEssential, appliedGame, gameRules.Count));
        return new HealthSnapshot(
            report.Overall,
            report.StartupScore,
            report.BackgroundServiceScore,
            report.ReadinessScore,
            report.Grade,
            report.Suggestions);
    }

    private sealed record HealthSnapshot(
        int Total,
        int StartupScore,
        int BackgroundScore,
        int ReadinessScore,
        string Grade,
        IReadOnlyList<string> Suggestions);
}
