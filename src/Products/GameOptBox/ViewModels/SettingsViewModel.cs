// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PcOpt.Core.Config;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 设置页视图模型：主题说明 / 规则包信息 / 更新检查（默认关，合规红线 #8）/ 配置导出导入 / 关于。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly RuleRepository _rules;
    private readonly UpdateChecker _update;
    private readonly UserConfigService _config;
    private readonly INotificationService _notifications;

    /// <summary>初始化设置页。</summary>
    public SettingsViewModel(
        RuleRepository rules,
        UpdateChecker update,
        UserConfigService config,
        INotificationService notifications)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _update = update ?? throw new ArgumentNullException(nameof(update));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionText = $"游优化 GameOptBox v{version}";
        RulePackageText = $"已加载 {_rules.Count} 项官方签名规则";
        ConfigPath = _config.GetConfigFilePath(ResourcePaths.ProductName);
    }

    /// <summary>版本信息。</summary>
    [ObservableProperty]
    private string _versionText;

    /// <summary>规则包信息。</summary>
    [ObservableProperty]
    private string _rulePackageText;

    /// <summary>配置文件路径。</summary>
    [ObservableProperty]
    private string _configPath;

    /// <summary>更新检查结果。</summary>
    [ObservableProperty]
    private string _updateResult = "未检查（默认离线）";

    /// <summary>是否启动时自动检查更新（默认关闭，PRD 8#7 默认完全离线）。</summary>
    [ObservableProperty]
    private bool _autoCheckUpdate;

    partial void OnAutoCheckUpdateChanged(bool value)
    {
        _update.AutoCheckEnabled = value;
        _notifications.Notify(value ? "已开启启动时检查更新（可随时关闭）" : "已关闭更新检查，保持离线");
    }

    /// <summary>手动检查更新（唯一允许的联网操作，默认不自动执行）。</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        UpdateResult = "正在检查…";
        var result = await _update.CheckAsync().ConfigureAwait(true);
        UpdateResult = result.IsSuccess ? result.Value! : result.Error;
    }

    /// <summary>导出用户配置（JSON）。</summary>
    [RelayCommand]
    private void ExportConfig()
    {
        var save = _config.Save(new UserConfig { Product = ResourcePaths.ProductName });
        if (!save.IsSuccess)
        {
            _notifications.Notify(save.Error);
            return;
        }

        var dialog = new SaveFileDialog { FileName = "GameOptBox-config.json", Filter = "JSON 配置|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.Copy(_config.GetConfigFilePath(ResourcePaths.ProductName), dialog.FileName, overwrite: true);
            _notifications.Notify($"已导出配置到 {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Notify($"导出配置失败：{ex.Message}");
        }
    }

    /// <summary>导入用户配置（schema 校验 + 逐项合并）。</summary>
    [RelayCommand]
    private void ImportConfig()
    {
        var dialog = new OpenFileDialog { Filter = "JSON 配置|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var current = _config.Load(ResourcePaths.ProductName);
            if (!current.IsSuccess)
            {
                _notifications.Notify(current.Error);
                return;
            }

            var merged = _config.Import(current.Value!, json);
            if (!merged.IsSuccess)
            {
                _notifications.Notify(merged.Error);
                return;
            }

            var saved = _config.Save(merged.Value!);
            _notifications.Notify(saved.IsSuccess ? "已导入配置（已逐项校验）" : saved.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Notify($"导入配置失败：{ex.Message}");
        }
    }

    /// <summary>打开数据目录。</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(ResourcePaths.DataDirectory);
            Process.Start(new ProcessStartInfo(ResourcePaths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _notifications.Notify($"打开数据目录失败：{ex.Message}");
        }
    }
}
