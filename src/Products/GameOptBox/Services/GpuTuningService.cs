// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging;
using PcOpt.Core.Platform;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>一条显卡调优说明（不含可应用规则，仅人话指引）。</summary>
/// <param name="Title">标题。</param>
/// <param name="Description">人话说明。</param>
public sealed record GpuGuidance(string Title, string Description);

/// <summary>
/// GPU 合规调优服务（T26，PRD B-P0-6）。
/// 提供厂商识别与"分厂商"人话指引；可应用的调优项（HAGS / MPO / TDR）来自官方签名规则，
/// 由 <see cref="ViewModels.GpuTuningViewModel"/> 经 <see cref="RuleRepository"/> 应用。
/// <b>合规红线 #1</b>：本服务不做任何显卡型号伪装，硬件信息只真实展示。
/// </summary>
public sealed class GpuTuningService
{
    private readonly ILogger<GpuTuningService> _logger;

    /// <summary>初始化 GPU 调优服务。</summary>
    public GpuTuningService(ISystemEnvironment environment, ILogger<GpuTuningService> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Vendor = string.IsNullOrWhiteSpace(environment.GpuVendor) ? "未知" : environment.GpuVendor;
    }

    /// <summary>当前显卡厂商（NVIDIA / AMD / Intel / 未知）。</summary>
    public string Vendor { get; }

    /// <summary>厂商人话文案。</summary>
    public string VendorDisplay => Vendor switch
    {
        "NVIDIA" => "NVIDIA 显卡",
        "AMD" => "AMD 显卡",
        "Intel" => "Intel 显卡",
        _ => "显卡厂商未识别",
    };

    /// <summary>推荐的官方规则 Id（HAGS / MPO 修复 / TDR）——按厂商与通用性给出。</summary>
    public IReadOnlyList<string> RecommendedRuleIds { get; } = new[]
    {
        "gpu.hags.enable",
        "latency.dwm.mpo.disable",
        "gpu.tdr.delay.increase",
    };

    /// <summary>分厂商 / 通用的人话指引（dGPU 直连、驱动面板设置等，无型号伪装）。</summary>
    public IReadOnlyList<GpuGuidance> BuildGuidance()
    {
        var guidance = new List<GpuGuidance>
        {
            new(
                "独显直连（如机型支持）",
                "在笔记本的 BIOS / 厂商控制中心里可切换“独显直连”，让画面不经核显中转，降低延迟。此为硬件开关，本工具不代为修改。"),
            new(
                "MPO 卡顿修复",
                "部分显卡在开启多平面叠加（MPO）后会出现花屏 / 卡顿，关闭 MPO 可修复（本工具提供一键开关，可随时还原）。"),
            new(
                "厂商驱动面板",
                "低延迟模式、垂直同步、纹理过滤等建议在显卡厂商控制面板（NVIDIA 控制面板 / AMD Software / Intel 显卡中心）中设置。"),
        };

        switch (Vendor)
        {
            case "NVIDIA":
                guidance.Add(new GpuGuidance(
                    "NVIDIA 低延迟",
                    "在 NVIDIA 控制面板把“低延迟模式”设为“超高”，并按需开启硬件加速 GPU 调度（HAGS）。"));
                break;
            case "AMD":
                guidance.Add(new GpuGuidance(
                    "AMD 抗延迟",
                    "在 AMD Software 开启 Radeon Anti-Lag，并按需开启硬件加速 GPU 调度（HAGS）。"));
                break;
            case "Intel":
                guidance.Add(new GpuGuidance(
                    "Intel Arc 调优",
                    "在 Intel 显卡中心开启 Resizable BAR 支持并启用 XeSS；HAGS 对部分游戏有效。"));
                break;
            default:
                _logger.LogDebug("显卡厂商未识别，仅展示通用指引");
                break;
        }

        return guidance;
    }
}
