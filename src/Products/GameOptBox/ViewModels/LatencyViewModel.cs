// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// 延迟优化组视图模型（T24，PRD B-P0-4）：计时器分辨率 / 鼠标 1:1 / USB 选择性暂停 / 全屏优化 / MPO；
/// USB 轮询率与音频独占以人话指引呈现（不改设备、不写入驱动）。
/// </summary>
public sealed partial class LatencyViewModel : ObservableObject, IRefreshable
{
    private static readonly string[] RuleIds =
    {
        "latency.timer.resolution.set",
        "latency.mouse.acceleration.disable",
        "latency.usb.selective.suspend.disable",
        "game.fullscreen.optimization.disable",
        "latency.dwm.mpo.disable",
        "latency.keyboard.repeat.delay.min",
    };

    private readonly RuleRepository _rules;
    private readonly RuleActionCoordinator _coordinator;

    /// <summary>初始化延迟优化页。</summary>
    public LatencyViewModel(RuleRepository rules, RuleActionCoordinator coordinator)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        Refresh();
    }

    /// <summary>延迟优化规则卡片。</summary>
    public ObservableCollection<RuleItemViewModel> Items { get; } = new();

    /// <summary>USB 轮询率指引。</summary>
    [ObservableProperty]
    private string _usbNote = "USB 轮询率：请在键鼠厂商驱动软件中把回报率设为 1000Hz（本工具不直接写入设备，避免不兼容）。";

    /// <summary>音频独占指引。</summary>
    [ObservableProperty]
    private string _audioNote = "音频独占：在“声音设置 → 设备属性 → 高级”中勾选“允许应用独占控制此设备”，可减少音频延迟。";

    /// <summary>汇总文案。</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <inheritdoc/>
    public void Refresh()
    {
        var map = _rules.Rules.ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        foreach (var id in RuleIds)
        {
            if (map.TryGetValue(id, out var rule))
            {
                Items.Add(new RuleItemViewModel(rule, _rules.IsApplied(rule.Id), _coordinator));
            }
        }

        Summary = $"共 {Items.Count} 项可调优 · 每项都有前后效果人话说明，且随时可还原";
    }
}
