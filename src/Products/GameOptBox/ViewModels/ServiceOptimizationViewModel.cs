// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 服务优化页视图模型（T22，PRD B-P0-1）：服务列表（名称 / 状态 / 人话说明 / 推荐值）+
/// 一键"游戏模式"方案（整组可还原）。
/// </summary>
public sealed partial class ServiceOptimizationViewModel : ObservableObject, IRefreshable
{
    private readonly ServiceCatalogService _catalog;
    private readonly RuleRepository _rules;
    private readonly RuleActionCoordinator _coordinator;
    private readonly INotificationService _notifications;
    private IReadOnlyList<ServiceEntry> _all = Array.Empty<ServiceEntry>();

    /// <summary>初始化服务优化页。</summary>
    public ServiceOptimizationViewModel(
        ServiceCatalogService catalog,
        RuleRepository rules,
        RuleActionCoordinator coordinator,
        INotificationService notifications)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>当前展示的服务（经筛选）。</summary>
    public ObservableCollection<ServiceEntryViewModel> Items { get; } = new();

    /// <summary>筛选关键词。</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>仅显示可关闭的非必要服务。</summary>
    [ObservableProperty]
    private bool _onlyNonEssential = true;

    /// <summary>汇总文案。</summary>
    [ObservableProperty]
    private string _summary = "正在读取系统服务…";

    /// <summary>服务总数（用户态）。</summary>
    [ObservableProperty]
    private int _serviceCount;

    /// <summary>一键方案的目标服务数。</summary>
    [ObservableProperty]
    private int _planTargetCount;

    /// <inheritdoc/>
    public void Refresh() => _ = RefreshAsync();

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnOnlyNonEssentialChanged(bool value) => ApplyFilter();

    /// <summary>读取系统服务（后台线程）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var result = await Task.Run(() => _catalog.Enumerate()).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _all = Array.Empty<ServiceEntry>();
            Summary = result.Error;
            Items.Clear();
            return;
        }

        _all = result.Value!;
        ServiceCount = _all.Count;
        PlanTargetCount = ServiceOptimizationPlanner.SelectTargets(_all).Count;
        ApplyFilter();
    }

    /// <summary>应用一键"游戏模式"服务方案（整组可还原）。</summary>
    [RelayCommand]
    private async Task ApplyPlanAsync()
    {
        var plan = ServiceOptimizationPlanner.Plan(_all);
        if (plan.Targets.Count == 0)
        {
            _notifications.Notify("没有需要调整的服务，当前已是推荐设置");
            return;
        }

        var register = _rules.RegisterRule(plan.Rule);
        if (!register.IsSuccess)
        {
            _notifications.Notify(register.Error);
            return;
        }

        if (await _coordinator.ApplyAsync(plan.Rule).ConfigureAwait(true))
        {
            Refresh();
        }
    }

    private void ApplyFilter()
    {
        var query = FilterText?.Trim();
        var source = OnlyNonEssential ? _all.Where(entry => entry.NonEssential) : _all;
        if (!string.IsNullOrEmpty(query))
        {
            source = source.Where(entry =>
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Items.Clear();
        foreach (var entry in source)
        {
            Items.Add(new ServiceEntryViewModel(entry));
        }

        Summary = $"共 {ServiceCount} 个系统服务，当前显示 {Items.Count} 个 · 一键方案将调整 {PlanTargetCount} 项";
    }
}
