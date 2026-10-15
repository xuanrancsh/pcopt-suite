// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>
/// GPU 合规调优页视图模型（T26，PRD B-P0-6）：HAGS 开关 / MPO 修复 / TDR 延迟等可应用规则 +
/// 分厂商人话指引。<b>合规红线 #1</b>：只真实展示硬件，不做型号伪装。
/// </summary>
public sealed partial class GpuTuningViewModel : ObservableObject, IRefreshable
{
    private readonly RuleRepository _rules;
    private readonly RuleActionCoordinator _coordinator;
    private readonly GpuTuningService _gpu;

    /// <summary>初始化 GPU 调优页。</summary>
    public GpuTuningViewModel(RuleRepository rules, RuleActionCoordinator coordinator, GpuTuningService gpu)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _gpu = gpu ?? throw new ArgumentNullException(nameof(gpu));
        Refresh();
    }

    /// <summary>可应用的 GPU 调优规则。</summary>
    public ObservableCollection<RuleItemViewModel> Items { get; } = new();

    /// <summary>分厂商 / 通用人话指引。</summary>
    public ObservableCollection<GpuGuidance> Guidance { get; } = new();

    /// <summary>厂商文案。</summary>
    [ObservableProperty]
    private string _vendorText = string.Empty;

    /// <inheritdoc/>
    public void Refresh()
    {
        var map = _rules.Rules.ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        foreach (var id in _gpu.RecommendedRuleIds)
        {
            if (map.TryGetValue(id, out var rule))
            {
                Items.Add(new RuleItemViewModel(rule, _rules.IsApplied(rule.Id), _coordinator));
            }
        }

        Guidance.Clear();
        foreach (var guidance in _gpu.BuildGuidance())
        {
            Guidance.Add(guidance);
        }

        VendorText = $"{_gpu.VendorDisplay} · 本工具只真实展示硬件信息，不做任何型号伪装（合规红线 #1）";
    }
}
