// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 启动项管理视图模型（T23，PRD B-P0-3）：注册表 + 启动文件夹 + 计划任务三路扫描，
/// 启用 / 禁用可逆（禁用不删除，随时恢复）。
/// </summary>
public sealed partial class StartupItemsViewModel : ObservableObject, IRefreshable
{
    private readonly StartupItemService _service;
    private readonly INotificationService _notifications;

    /// <summary>初始化启动项管理页。</summary>
    public StartupItemsViewModel(StartupItemService service, INotificationService notifications)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>启动项列表。</summary>
    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    /// <summary>汇总文案。</summary>
    [ObservableProperty]
    private string _summary = "正在扫描启动项…";

    /// <inheritdoc/>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>三路扫描启动项（后台线程）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var result = await Task.Run(() => _service.Scan()).ConfigureAwait(true);
        Items.Clear();
        if (!result.IsSuccess)
        {
            Summary = result.Error;
            return;
        }

        foreach (var item in result.Value!)
        {
            Items.Add(new StartupItemViewModel(item, _service, _notifications, UpdateSummary));
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var enabled = Items.Count(item => item.IsEnabled);
        Summary = $"共 {Items.Count} 个启动项，其中 {enabled} 个已启用 · 禁用后随时可恢复";
    }
}
