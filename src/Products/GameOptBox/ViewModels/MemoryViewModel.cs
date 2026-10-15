// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Core;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 内存清理页视图模型（T27，PRD B-P0-7）：工作集修剪 / 待机列表清理 / 一键清理，
/// 清理量实时显示。
/// </summary>
public sealed partial class MemoryViewModel : ObservableObject, IRefreshable
{
    private readonly IMemoryManager _memory;
    private readonly INotificationService _notifications;

    /// <summary>初始化内存清理页。</summary>
    public MemoryViewModel(IMemoryManager memory, INotificationService notifications)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>物理内存总量文案。</summary>
    [ObservableProperty]
    private string _totalText = "—";

    /// <summary>可用内存文案。</summary>
    [ObservableProperty]
    private string _availableText = "—";

    /// <summary>已用内存文案。</summary>
    [ObservableProperty]
    private string _usedText = "—";

    /// <summary>上次清理结果。</summary>
    [ObservableProperty]
    private string _lastResult = "点击下方按钮开始清理";

    /// <inheritdoc/>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>刷新内存状态。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var status = await Task.Run(() => _memory.Query()).ConfigureAwait(true);
        Update(status);
    }

    /// <summary>修剪全部进程工作集。</summary>
    [RelayCommand]
    private async Task TrimWorkingSetsAsync()
    {
        var result = await Task.Run(() => _memory.TrimWorkingSets()).ConfigureAwait(true);
        Handle(result, "已修剪工作集");
    }

    /// <summary>清空待机列表。</summary>
    [RelayCommand]
    private async Task PurgeStandbyListAsync()
    {
        var result = await Task.Run(() => _memory.PurgeStandbyList()).ConfigureAwait(true);
        Handle(result, "已清理待机列表");
    }

    /// <summary>一键清理（工作集 + 待机列表）。</summary>
    [RelayCommand]
    private async Task CleanAllAsync()
    {
        var trim = await Task.Run(() => _memory.TrimWorkingSets()).ConfigureAwait(true);
        var purge = await Task.Run(() => _memory.PurgeStandbyList()).ConfigureAwait(true);
        var after = purge.IsSuccess ? purge.Value!.After : trim.IsSuccess ? trim.Value!.After : _memory.Query();
        var freed = (trim.IsSuccess ? trim.Value!.FreedBytes : 0) + (purge.IsSuccess ? purge.Value!.FreedBytes : 0);
        Update(after);
        LastResult = $"本次释放约 {Humanize.Size(freed)} · 可用内存 {Humanize.Size(after.AvailableBytes)}";
        _notifications.Notify(purge.IsSuccess || trim.IsSuccess
            ? $"内存清理完成，释放约 {Humanize.Size(freed)}"
            : "内存清理失败（待机列表清理需要管理员权限）");
    }

    private void Handle(Result<MemoryTrimResult> result, string action)
    {
        if (!result.IsSuccess)
        {
            _notifications.Notify(result.Error);
            LastResult = result.Error;
            return;
        }

        Update(result.Value!.After);
        LastResult = $"{action}，释放约 {Humanize.Size(result.Value.FreedBytes)} · 可用内存 {Humanize.Size(result.Value.After.AvailableBytes)}";
    }

    private void Update(MemoryStatus status)
    {
        TotalText = Humanize.Size(status.TotalPhysicalBytes);
        AvailableText = Humanize.Size(status.AvailableBytes);
        UsedText = Humanize.Size(status.UsedBytes);
    }
}
