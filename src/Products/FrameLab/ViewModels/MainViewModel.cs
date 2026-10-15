// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>
/// 主界面导航视图模型（T31，PRD 4.5 信息架构：测试台 / 报告 / 诊断 / 历史 / 设置）。
/// 选中导航即切换内容区；采集状态变化时刷新各模块（共享 <see cref="CaptureSessionStore"/>）。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly CaptureSessionStore _store;

    /// <summary>初始化主界面导航。</summary>
    public MainViewModel(
        TestBenchViewModel testBench,
        ReportViewModel report,
        DiagnoseViewModel diagnose,
        HistoryViewModel history,
        SettingsViewModel settings,
        CaptureSessionStore store)
    {
        ArgumentNullException.ThrowIfNull(testBench);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(diagnose);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(settings);
        _store = store ?? throw new ArgumentNullException(nameof(store));

        NavItems = new ObservableCollection<NavItemViewModel>
        {
            new("测试台", "🎯", testBench),
            new("报告", "📄", report),
            new("诊断", "🩺", diagnose),
            new("历史", "🕘", history),
            new("设置", "⚙️", settings),
        };

        _store.Changed += OnStoreChanged;
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

    partial void OnSelectedNavChanged(NavItemViewModel? value)
    {
        CurrentPage = value?.Page;
        if (value?.Page is IRefreshable refreshable)
        {
            refreshable.Refresh();
        }
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        foreach (var nav in NavItems)
        {
            if (nav.Page is IRefreshable refreshable)
            {
                refreshable.Refresh();
            }
        }
    }
}

/// <summary>可刷新模块契约（导航切换 / 状态变化时重新加载数据）。</summary>
public interface IRefreshable
{
    /// <summary>刷新模块数据。</summary>
    void Refresh();
}
