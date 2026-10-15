// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 主界面导航视图模型（T20，PRD 3.6 左侧导航 11 模块）。
/// 选中导航项即切换内容区；"系统优化"显示"已生效 N 项"徽标（PRD 5.4）。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly RuleRepository _rules;

    /// <summary>初始化主界面导航。</summary>
    public MainViewModel(
        HomeViewModel home,
        RuleBrowserViewModel ruleBrowser,
        ServiceOptimizationViewModel services,
        StartupItemsViewModel startup,
        LatencyViewModel latency,
        PowerPlanViewModel power,
        GpuTuningViewModel gpu,
        MemoryViewModel memory,
        CleanupViewModel cleanup,
        RestoreCenterViewModel restore,
        SettingsViewModel settings,
        RuleRepository rules)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(ruleBrowser);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(latency);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(settings);
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));

        NavItems = new ObservableCollection<NavItemViewModel>
        {
            new("首页", "🏠", home),
            new("系统优化", "⚙️", ruleBrowser),
            new("服务优化", "🔧", services),
            new("启动项", "🚀", startup),
            new("游戏优化", "🎮", latency),
            new("电源计划", "⚡", power),
            new("显卡调优", "🖥️", gpu),
            new("内存清理", "🧹", memory),
            new("卸载清理", "🗑️", cleanup),
            new("还原中心", "↩️", restore),
            new("设置", "⚙️", settings),
        };

        _rules.AppliedStateChanged += OnAppliedStateChanged;
        RefreshBadges();
        SelectedNav = NavItems[0];
    }

    /// <summary>导航项集合。</summary>
    public ObservableCollection<NavItemViewModel> NavItems { get; }

    /// <summary>当前选中的导航项。</summary>
    [ObservableProperty]
    private NavItemViewModel? _selectedNav;

    /// <summary>内容区当前模块视图模型。</summary>
    [ObservableProperty]
    private object? _currentPage;

    /// <summary>刷新导航徽标与当前模块（供首页"一键体检修复"后调用）。</summary>
    public void RefreshAll()
    {
        RefreshBadges();
        foreach (var nav in NavItems)
        {
            if (ReferenceEquals(nav, SelectedNav) && nav.Page is IRefreshable refreshable)
            {
                refreshable.Refresh();
            }
        }
    }

    partial void OnSelectedNavChanged(NavItemViewModel? value)
    {
        CurrentPage = value?.Page;
        if (value?.Page is IRefreshable refreshable)
        {
            refreshable.Refresh();
        }
    }

    private void OnAppliedStateChanged(object? sender, EventArgs e) => RefreshBadges();

    private void RefreshBadges()
    {
        // "系统优化"显示"已生效 N 项"徽标（PRD 5.4）
        if (NavItems.Count > 1)
        {
            NavItems[1].SetBadge(_rules.CountApplied());
        }
    }
}

/// <summary>可刷新模块契约（导航切换 / 状态变化时重新加载数据）。</summary>
public interface IRefreshable
{
    /// <summary>刷新模块数据。</summary>
    void Refresh();
}
