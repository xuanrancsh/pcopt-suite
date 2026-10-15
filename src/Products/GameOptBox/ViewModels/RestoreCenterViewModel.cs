// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 还原中心视图模型（T29，PRD B-P0-9）：单条还原 / 全部还原 / 创建系统还原点 / 操作日志导出。
/// 与内核 Journal 同一实现，还原后校验通过。
/// </summary>
public sealed partial class RestoreCenterViewModel : ObservableObject, IRefreshable
{
    private readonly RestoreCenterService _restore;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    /// <summary>初始化还原中心。</summary>
    public RestoreCenterViewModel(
        RestoreCenterService restore,
        IDialogService dialogs,
        INotificationService notifications)
    {
        _restore = restore ?? throw new ArgumentNullException(nameof(restore));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <summary>当前已应用的规则。</summary>
    public ObservableCollection<AppliedRuleItemViewModel> AppliedRules { get; } = new();

    /// <summary>还原日志会话。</summary>
    public ObservableCollection<JournalSessionItemViewModel> Sessions { get; } = new();

    /// <summary>已应用汇总。</summary>
    [ObservableProperty]
    private string _summary = "正在读取…";

    /// <summary>会话汇总。</summary>
    [ObservableProperty]
    private string _sessionSummary = string.Empty;

    /// <inheritdoc/>
    public void Refresh() => _ = RefreshAsync();

    /// <summary>读取已应用规则与会话（后台线程）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var applied = await Task.Run(() => _restore.ListAppliedRules()).ConfigureAwait(true);
        AppliedRules.Clear();
        foreach (var info in applied)
        {
            AppliedRules.Add(new AppliedRuleItemViewModel(info, RevertRuleAsync));
        }

        Summary = AppliedRules.Count > 0
            ? $"当前有 {AppliedRules.Count} 项优化生效，可单条或全部还原"
            : "当前没有生效中的优化，电脑状态与改动前一致";

        var sessions = await Task.Run(() => _restore.ListSessions()).ConfigureAwait(true);
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(new JournalSessionItemViewModel(session));
        }

        SessionSummary = $"共 {Sessions.Count} 条操作记录（还原日志，可审计）";
    }

    /// <summary>还原单条规则。</summary>
    private async Task RevertRuleAsync(AppliedRuleItemViewModel viewModel)
    {
        var decision = _dialogs.Confirm(
            "确认还原",
            $"即将还原“{viewModel.Title}”。",
            new[] { "还原后此项改动回到改动前的状态", "还原过程按记录的原值逐项恢复" });
        if (!decision.Confirmed)
        {
            return;
        }

        var result = await Task.Run(() => _restore.RevertRule(viewModel.RuleId)).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess
            ? $"已还原“{viewModel.Title}”，共 {result.Value!.EntriesReverted} 项"
            : $"还原失败：{result.Error}");
        Refresh();
    }

    /// <summary>全部还原。</summary>
    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (AppliedRules.Count == 0)
        {
            _notifications.Notify("当前没有需要还原的项目");
            return;
        }

        var decision = _dialogs.Confirm(
            "确认全部还原",
            $"将把当前 {AppliedRules.Count} 项优化全部还原到改动前的状态。",
            new[] { "逐项按记录的原值恢复", "还原后电脑回到改动前的状态" });
        if (!decision.Confirmed)
        {
            return;
        }

        var result = await Task.Run(() => _restore.RevertAll()).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess
            ? $"已全部还原，共 {result.Value!.RevertedEntries} 项，你的电脑恢复正常了"
            : $"还原未完成：{result.Error}");
        Refresh();
    }

    /// <summary>创建系统还原点。</summary>
    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        var result = await Task.Run(() => _restore.CreateRestorePoint("pcopt 优化前还原点")).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }

    /// <summary>导出操作日志。</summary>
    [RelayCommand]
    private void ExportLog()
    {
        var directory = Path.Combine(ResourcePaths.DataDirectory, "logs");
        var path = Path.Combine(directory, $"operation-log-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var result = _restore.ExportLog(path);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
    }
}
