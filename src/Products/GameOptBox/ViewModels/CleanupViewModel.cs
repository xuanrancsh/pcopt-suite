// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 卸载与清理页视图模型（T28，PRD B-P0-8）：Appx 预装应用卸载（风险分级 + 可重装）、
/// 残留预览、垃圾清理（勾选类别，先预览后删除，零误删）。
/// </summary>
public sealed partial class CleanupViewModel : ObservableObject, IRefreshable
{
    private readonly JunkCleanupService _junk;
    private readonly AppxPackageService _appx;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;

    /// <summary>初始化卸载清理页。</summary>
    public CleanupViewModel(
        JunkCleanupService junk,
        AppxPackageService appx,
        IDialogService dialogs,
        INotificationService notifications)
    {
        _junk = junk ?? throw new ArgumentNullException(nameof(junk));
        _appx = appx ?? throw new ArgumentNullException(nameof(appx));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        foreach (var category in _junk.Categories)
        {
            JunkCategories.Add(new JunkCategoryViewModel(category));
        }
    }

    /// <summary>垃圾清理类别。</summary>
    public ObservableCollection<JunkCategoryViewModel> JunkCategories { get; } = new();

    /// <summary>Appx 包列表。</summary>
    public ObservableCollection<AppxPackageViewModel> Packages { get; } = new();

    /// <summary>垃圾汇总文案。</summary>
    [ObservableProperty]
    private string _junkSummary = "正在扫描…";

    /// <summary>Appx 汇总文案。</summary>
    [ObservableProperty]
    private string _appxSummary = "正在扫描…";

    /// <inheritdoc/>
    public void Refresh()
    {
        _ = ScanJunkAsync();
        _ = ScanAppxAsync();
    }

    /// <summary>扫描垃圾（预览）。</summary>
    [RelayCommand]
    private async Task ScanJunkAsync()
    {
        var infos = await Task.Run(() => _junk.Scan()).ConfigureAwait(true);
        var map = infos.ToDictionary(info => info.Category.Key, StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var vm in JunkCategories)
        {
            if (map.TryGetValue(vm.Category.Key, out var info))
            {
                vm.Update(info);
                total += info.SizeBytes;
            }
        }

        JunkSummary = $"扫描到约 {Humanize.Size(total)} 可清理，勾选后点“清理选中项”";
    }

    /// <summary>清理选中的垃圾类别。</summary>
    [RelayCommand]
    private async Task CleanJunkAsync()
    {
        var keys = JunkCategories.Where(vm => vm.IsSelected).Select(vm => vm.Category.Key).ToList();
        if (keys.Count == 0)
        {
            _notifications.Notify("请先勾选要清理的类别");
            return;
        }

        var decision = _dialogs.Confirm(
            "确认清理",
            "将删除选中类别中的临时 / 缓存文件（占用中的文件会自动跳过）。",
            new[] { "只清理临时与缓存文件，不碰个人文档", "删除后一般不影响使用，个别程序会重新生成缓存" });
        if (!decision.Confirmed)
        {
            return;
        }

        var result = await Task.Run(() => _junk.Clean(keys)).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _notifications.Notify(result.Error);
            return;
        }

        _notifications.Notify($"清理完成，释放约 {Humanize.Size(result.Value)}");
        await ScanJunkAsync().ConfigureAwait(true);
    }

    /// <summary>扫描 Appx 包。</summary>
    [RelayCommand]
    private async Task ScanAppxAsync()
    {
        var result = await Task.Run(() => _appx.Scan()).ConfigureAwait(true);
        Packages.Clear();
        if (!result.IsSuccess)
        {
            AppxSummary = result.Error;
            return;
        }

        foreach (var package in result.Value!)
        {
            Packages.Add(new AppxPackageViewModel(package, RemoveAppxAsync, PreviewResidual));
        }

        var removable = result.Value!.Count(package => package.CanRemove);
        AppxSummary = $"共 {Packages.Count} 个应用，其中 {removable} 个可卸载（系统组件已标注且不可卸载）";
    }

    private async Task RemoveAppxAsync(AppxPackageViewModel viewModel)
    {
        if (!viewModel.CanRemove)
        {
            _notifications.Notify($"“{viewModel.Name}”是系统组件，为安全起见不支持卸载");
            return;
        }

        var decision = _dialogs.Confirm(
            "确认卸载应用",
            $"即将卸载“{viewModel.Name}”。",
            new[] { viewModel.Recommendation, "卸载后可在应用商店重新安装" });
        if (!decision.Confirmed)
        {
            return;
        }

        var result = await Task.Run(() => _appx.Remove(viewModel.Package)).ConfigureAwait(true);
        _notifications.Notify(result.IsSuccess ? result.Value! : result.Error);
        await ScanAppxAsync().ConfigureAwait(true);
    }

    private void PreviewResidual(AppxPackageViewModel viewModel)
    {
        var residual = _appx.PreviewResidual(viewModel.Name);
        _notifications.Notify(residual.Count > 0
            ? $"发现 {residual.Count} 处残留数据目录（仅预览，不会自动删除）"
            : "未发现残留数据目录");
    }
}
