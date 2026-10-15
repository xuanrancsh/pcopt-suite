// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>人类可读格式化（文件大小 / 字节数）。</summary>
public static class Humanize
{
    /// <summary>把字节数格式化为 B / KB / MB / GB。</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    /// <summary>风险等级中文文案。</summary>
    public static string Risk(RiskLevel risk) => risk switch
    {
        RiskLevel.Safe => "安全",
        RiskLevel.Notice => "注意",
        RiskLevel.Danger => "危险",
        _ => "未知",
    };
}

/// <summary>服务目录列表项（T22）。</summary>
public sealed class ServiceEntryViewModel
{
    /// <summary>初始化服务项。</summary>
    public ServiceEntryViewModel(ServiceEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    /// <summary>底层服务条目。</summary>
    public ServiceEntry Entry { get; }

    /// <summary>显示名。</summary>
    public string DisplayName => Entry.DisplayName;

    /// <summary>服务名。</summary>
    public string Name => Entry.Name;

    /// <summary>人话说明。</summary>
    public string Description => Entry.Description;

    /// <summary>当前启动类型（中文）。</summary>
    public string CurrentStartText => StartText(Entry.CurrentStart);

    /// <summary>推荐启动类型（中文）。</summary>
    public string RecommendedStartText => StartText(Entry.RecommendedStart);

    /// <summary>是否非必要（可关闭）。</summary>
    public bool NonEssential => Entry.NonEssential;

    /// <summary>当前与推荐是否一致。</summary>
    public bool AlreadyOptimal => Entry.CurrentStart == Entry.RecommendedStart;

    private static string StartText(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Boot => "引导启动",
        ServiceStartMode.System => "系统启动",
        ServiceStartMode.Automatic => "自动",
        ServiceStartMode.Manual => "手动",
        ServiceStartMode.Disabled => "已禁用",
        _ => "未知",
    };
}

/// <summary>启动项列表项（T23）。</summary>
public sealed partial class StartupItemViewModel : ObservableObject
{
    private readonly StartupItemService _service;
    private readonly INotificationService _notifications;
    private readonly Action _onChanged;
    private readonly bool _initialEnabled;
    private bool _suppress;

    /// <summary>初始化启动项。</summary>
    public StartupItemViewModel(
        StartupItem item,
        StartupItemService service,
        INotificationService notifications,
        Action onChanged)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _initialEnabled = item.Enabled;
        _isEnabled = item.Enabled;
    }

    /// <summary>底层启动项。</summary>
    public StartupItem Item { get; }

    /// <summary>名称。</summary>
    public string Name => Item.Name;

    /// <summary>来源（中文）。</summary>
    public string SourceText => Item.SourceText;

    /// <summary>命令 / 路径。</summary>
    public string Command => Item.Command;

    /// <summary>发布者。</summary>
    public string PublisherText => string.IsNullOrWhiteSpace(Item.Publisher) ? "发布者未知" : Item.Publisher;

    /// <summary>是否启用（绑定复选框；变化即执行启用 / 禁用）。</summary>
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress)
        {
            return;
        }

        var result = value ? _service.Enable(Item) : _service.Disable(Item);
        if (!result.IsSuccess)
        {
            _notifications.Notify(result.Error);
            _suppress = true;
            IsEnabled = _initialEnabled;
            _suppress = false;
            return;
        }

        _notifications.Notify(value ? $"已启用“{Name}”（下次开机生效）" : $"已禁用“{Name}”（随时可恢复）");
        _onChanged();
    }
}

/// <summary>垃圾清理类别项（T28）。</summary>
public sealed partial class JunkCategoryViewModel : ObservableObject
{
    /// <summary>初始化类别项。</summary>
    public JunkCategoryViewModel(JunkCategory category)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }

    /// <summary>底层类别。</summary>
    public JunkCategory Category { get; }

    /// <summary>标题。</summary>
    public string Title => Category.Title;

    /// <summary>是否勾选（默认勾选）。</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>可清理大小（人话）。</summary>
    [ObservableProperty]
    private string _sizeText = "未扫描";

    /// <summary>文件数文案。</summary>
    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>用扫描结果刷新展示。</summary>
    public void Update(JunkCategoryInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        SizeText = Humanize.Size(info.SizeBytes);
        CountText = $"{info.FileCount} 个文件";
    }
}

/// <summary>Appx 包列表项（T28）。</summary>
public sealed class AppxPackageViewModel
{
    /// <summary>初始化 Appx 包项。</summary>
    public AppxPackageViewModel(
        AppxPackageInfo package,
        Func<AppxPackageViewModel, Task> remove,
        Action<AppxPackageViewModel> previewResidual)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        RemoveCommand = new AsyncRelayCommand(() => remove(this), () => Package.CanRemove);
        PreviewResidualCommand = new RelayCommand(() => previewResidual(this));
    }

    /// <summary>底层包信息。</summary>
    public AppxPackageInfo Package { get; }

    /// <summary>包名。</summary>
    public string Name => Package.Name;

    /// <summary>版本。</summary>
    public string Version => Package.Version;

    /// <summary>发布者。</summary>
    public string Publisher => string.IsNullOrWhiteSpace(Package.Publisher) ? "未知" : Package.Publisher;

    /// <summary>风险文案。</summary>
    public string RiskText => Package.RiskText;

    /// <summary>风险等级（供样式触发）。</summary>
    public AppxRiskLevel Risk => Package.Risk;

    /// <summary>是否可卸载。</summary>
    public bool CanRemove => Package.CanRemove;

    /// <summary>人话建议。</summary>
    public string Recommendation => Package.Recommendation;

    /// <summary>卸载命令。</summary>
    public IAsyncRelayCommand RemoveCommand { get; }

    /// <summary>残留预览命令。</summary>
    public IRelayCommand PreviewResidualCommand { get; }
}

/// <summary>还原中心的"已应用规则"列表项（T29）。</summary>
public sealed class AppliedRuleItemViewModel
{
    /// <summary>初始化已应用规则项。</summary>
    public AppliedRuleItemViewModel(AppliedRuleInfo info, Func<AppliedRuleItemViewModel, Task> revert)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        RevertCommand = new AsyncRelayCommand(() => revert(this));
    }

    /// <summary>底层信息。</summary>
    public AppliedRuleInfo Info { get; }

    /// <summary>人话名。</summary>
    public string Title => Info.DisplayName;

    /// <summary>分类。</summary>
    public string Category => Info.Category;

    /// <summary>风险文案。</summary>
    public string RiskText => Humanize.Risk(Info.Risk);

    /// <summary>应用时间（本地）。</summary>
    public string AppliedAtText => Info.AppliedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");

    /// <summary>规则 Id。</summary>
    public string RuleId => Info.RuleId;

    /// <summary>还原命令。</summary>
    public IAsyncRelayCommand RevertCommand { get; }
}

/// <summary>还原中心的"还原日志会话"列表项（T29）。</summary>
public sealed class JournalSessionItemViewModel
{
    /// <summary>初始化会话项。</summary>
    public JournalSessionItemViewModel(JournalSessionInfo info)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
    }

    /// <summary>底层信息。</summary>
    public JournalSessionInfo Info { get; }

    /// <summary>标题（产品 · 状态）。</summary>
    public string Title => $"{Info.Product} · {StatusText(Info.Status)}";

    /// <summary>开始时间（本地）。</summary>
    public string TimeText => Info.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>明细（条目数）。</summary>
    public string DetailText => $"共 {Info.TotalEntries} 条记录，未还原 {Info.OpenEntries} 条";

    private static string StatusText(string status) => status switch
    {
        "Open" => "进行中",
        "Closed" => "已完成",
        "Recovered" => "已恢复",
        _ => status,
    };
}
