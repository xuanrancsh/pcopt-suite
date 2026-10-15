// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Game;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.ViewModels;

/// <summary>
/// 游戏列表视图模型（T14，PRD A-P0-1）：扫描六大平台游戏库并列表展示。
/// </summary>
public sealed partial class GamesViewModel : ObservableObject
{
    private readonly GameWatcher _watcher;
    private readonly ILogger<GamesViewModel> _logger;

    /// <summary>初始化游戏列表视图模型。</summary>
    public GamesViewModel(GameWatcher watcher, ILogger<GamesViewModel> logger)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>已识别的游戏。</summary>
    public ObservableCollection<GameListItemViewModel> Games { get; } = new();

    /// <summary>是否正在扫描。</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>列表是否为空（驱动空状态）。</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>状态文案。</summary>
    [ObservableProperty]
    private string _statusText = "尚未扫描，点击下方按钮开始扫描你已安装的游戏";

    /// <summary>扫描游戏库。</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        StatusText = "正在扫描游戏库…（Steam / Epic / GOG / Xbox / WeGame）";
        try
        {
            var result = await _watcher.ScanLibraryAsync().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusText = $"扫描失败：{result.Error}";
                _logger.LogWarning("游戏库扫描失败：{Error}", result.Error);
                return;
            }

            var report = result.Value!;
            Games.Clear();
            foreach (var game in report.Games.OrderBy(item => item.Name, StringComparer.CurrentCulture))
            {
                Games.Add(new GameListItemViewModel(
                    game.Name,
                    PlatformText(game.Platform),
                    "以运行时检测为准",
                    game.InstallPath ?? "未记录安装路径",
                    isRunning: false));
            }

            IsEmpty = Games.Count == 0;
            var platformNote = report.PlatformErrors.Count > 0
                ? $"（{report.PlatformErrors.Count} 个平台未能读取）"
                : string.Empty;
            StatusText = $"扫描完成：共识别 {Games.Count} 款游戏{platformNote}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static string PlatformText(GamePlatform platform) => platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic",
        GamePlatform.Gog => "GOG",
        GamePlatform.Xbox => "Xbox",
        GamePlatform.WeGame => "WeGame",
        _ => "其他平台",
    };
}
