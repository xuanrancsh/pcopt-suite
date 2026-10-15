// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 规则浏览器视图模型（T21，PRD B-P0-10 / 3.6）：全局搜索（≤200ms）、分类筛选、
/// 卡片三态、风险标签、危险项默认折叠且默认关闭（合规红线 #6）。
/// </summary>
public sealed partial class RuleBrowserViewModel : ObservableObject, IRefreshable
{
    private const string AllCategories = "全部分类";

    private readonly RuleRepository _rules;
    private readonly RuleActionCoordinator _coordinator;

    /// <summary>初始化规则浏览器。</summary>
    public RuleBrowserViewModel(RuleRepository rules, RuleActionCoordinator coordinator)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

        Categories.Add(AllCategories);
        foreach (var category in _rules.Categories)
        {
            Categories.Add(category);
        }

        Search();
    }

    /// <summary>当前筛选结果。</summary>
    public ObservableCollection<RuleItemViewModel> Items { get; } = new();

    /// <summary>分类下拉项（首项为"全部分类"）。</summary>
    public ObservableCollection<string> Categories { get; } = new();

    /// <summary>搜索关键词（变化即实时搜索）。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前分类。</summary>
    [ObservableProperty]
    private string _selectedCategory = AllCategories;

    /// <summary>是否显示敏感（危险）项（默认关闭，PRD 红线 #6）。</summary>
    [ObservableProperty]
    private bool _showSensitive;

    /// <summary>结果数。</summary>
    [ObservableProperty]
    private int _resultCount;

    /// <summary>上次搜索耗时（毫秒）。</summary>
    [ObservableProperty]
    private long _lastSearchElapsedMs;

    /// <summary>搜索摘要（结果数 + 耗时）。</summary>
    [ObservableProperty]
    private string _searchSummary = string.Empty;

    /// <inheritdoc/>
    public void Refresh() => Search();

    partial void OnSearchTextChanged(string value) => Search();

    partial void OnSelectedCategoryChanged(string value) => Search();

    partial void OnShowSensitiveChanged(bool value) => Search();

    /// <summary>执行搜索并重建卡片列表（记录耗时，验证 ≤200ms 指标）。</summary>
    [RelayCommand]
    private void Search()
    {
        var stopwatch = Stopwatch.StartNew();
        var category = string.Equals(SelectedCategory, AllCategories, StringComparison.Ordinal) ? null : SelectedCategory;
        var filtered = _rules.Search(SearchText, category, ShowSensitive);

        Items.Clear();
        foreach (var rule in filtered)
        {
            Items.Add(new RuleItemViewModel(rule, _rules.IsApplied(rule.Id), _coordinator));
        }

        stopwatch.Stop();
        ResultCount = Items.Count;
        LastSearchElapsedMs = stopwatch.ElapsedMilliseconds;
        SearchSummary = ShowSensitive
            ? $"共 {Items.Count} 项（含敏感项）· {LastSearchElapsedMs} ms"
            : $"共 {Items.Count} 项 · {LastSearchElapsedMs} ms · 危险项已折叠";
    }
}
