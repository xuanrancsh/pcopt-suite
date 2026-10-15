// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 电源计划页视图模型（T25，PRD B-P0-5）：列出方案 / 一键导入 Ultimate Performance / 激活方案。
/// </summary>
public sealed partial class PowerPlanViewModel : ObservableObject, IRefreshable
{
    private readonly PowerPlanService _power;
    private readonly INotificationService _notifications;

    /// <summary>初始化电源计划页。</summary>
    public PowerPlanViewModel(PowerPlanService power, INotificationService notifications)
    {
        _power = power ?? throw new ArgumentNullException(nameof(power));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>电源方案列表。</summary>
    public ObservableCollection<PowerSchemeInfo> Schemes { get; } = new();

    /// <summary>当前激活方案名。</summary>
    [ObservableProperty]
    private string _activeName = "读取中…";

    /// <summary>汇总文案。</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <inheritdoc/>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>读取电源方案（后台线程）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var result = await Task.Run(() => _power.List()).ConfigureAwait(true);
        Schemes.Clear();
        if (!result.IsSuccess)
        {
            ActiveName = "读取失败";
            Summary = result.Error;
            return;
        }

        foreach (var scheme in result.Value!)
        {
            Schemes.Add(scheme);
        }

        ActiveName = result.Value!.FirstOrDefault(scheme => scheme.IsActive)?.Name ?? "未知";
        Summary = $"共 {Schemes.Count} 个电源方案 · 当前：{ActiveName} · 切换后可在“还原中心”还原";
    }

    /// <summary>激活指定方案。</summary>
    [RelayCommand]
    private async Task ActivateAsync(PowerSchemeInfo? scheme)
    {
        if (scheme is null)
        {
            return;
        }

        var result = await Task.Run(() => _power.Activate(scheme.Guid)).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
        Refresh();
    }

    /// <summary>一键导入 Ultimate Performance 方案。</summary>
    [RelayCommand]
    private async Task ImportUltimateAsync()
    {
        var result = await Task.Run(() => _power.ImportUltimatePerformance()).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _notifications.Notify(result.Error);
            return;
        }

        _notifications.Notify("已导入“终极性能”电源方案，可点击激活");
        Refresh();
    }
}
