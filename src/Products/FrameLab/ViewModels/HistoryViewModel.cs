// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>
/// 历史视图模型（T31，PRD C-P1-3 轻量实现）：展示历史采集的关键指标，可清空。
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IRefreshable
{
    private readonly HistoryService _history;
    private readonly INotificationService _notifications;

    /// <summary>初始化历史视图模型。</summary>
    public HistoryViewModel(HistoryService history, INotificationService notifications)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        Refresh();
    }

    /// <summary>历史记录行。</summary>
    public ObservableCollection<HistoryRowViewModel> Entries { get; } = new();

    /// <summary>是否有历史记录。</summary>
    [ObservableProperty]
    private bool _hasEntries;

    /// <summary>总记录数人话。</summary>
    [ObservableProperty]
    private string _summaryText = "还没有历史记录";

    /// <summary>刷新历史列表。</summary>
    public void Refresh()
    {
        Entries.Clear();
        foreach (var entry in _history.Entries)
        {
            Entries.Add(new HistoryRowViewModel(entry));
        }

        HasEntries = Entries.Count > 0;
        SummaryText = HasEntries ? $"共 {Entries.Count} 条记录" : "还没有历史记录";
    }

    /// <summary>清空历史。</summary>
    [RelayCommand]
    private void Clear()
    {
        _history.Clear();
        Refresh();
        _notifications.Notify("已清空历史记录。");
    }
}
