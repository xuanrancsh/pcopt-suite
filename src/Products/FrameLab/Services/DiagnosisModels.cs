// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 诊断项的严重程度（决定 UI 配色；PRD 5.3 风险三级）。
/// </summary>
public enum DiagnosisSeverity
{
    /// <summary>提示（无害，可选）。</summary>
    Info,

    /// <summary>注意（建议处理）。</summary>
    Notice,

    /// <summary>危险（明显拖累体验）。</summary>
    Danger,
}

/// <summary>
/// 诊断输入（T35）：把"帧时间数据"与"系统状态信号"合并，便于纯函数化与单测注入。
/// </summary>
public sealed record DiagnosisInput
{
    /// <summary>本次采集的帧时间统计。</summary>
    public FrameStats Stats { get; init; } = FrameStats.Empty;

    /// <summary>待机列表是否膨胀（内存清理信号；由内存查询给出）。</summary>
    public bool StandbyListExpanded { get; init; }

    /// <summary>CPU 是否降频（电源计划信号；由 CPU 频率监控给出）。</summary>
    public bool CpuThrottling { get; init; }

    /// <summary>是否 GPU 瓶颈（帧时间长期贴着 GPU 忙时）。</summary>
    public bool GpuBottleneck { get; init; }

    /// <summary>是否已开启 HAGS（硬件加速 GPU 计划）。</summary>
    public bool HagsEnabled { get; init; }

    /// <summary>是否已开启 MPO。</summary>
    public bool MpoEnabled { get; init; } = true;

    /// <summary>GPU 厂商（NVIDIA / AMD / Intel；分厂商建议用）。</summary>
    public string GpuVendor { get; init; } = string.Empty;
}

/// <summary>
/// 一条诊断结论（PRD C-P0-4：每项给出"证据 + 建议 + 预期收益（人话）"）。
/// </summary>
/// <param name="SymptomKey">症状稳定键（用于去重 / 测试）。</param>
/// <param name="Symptom">症状人话描述。</param>
/// <param name="Evidence">证据列表（含具体数字，人话）。</param>
/// <param name="RecommendationId">建议稳定键（映射到规则 / 最小引擎动作）。</param>
/// <param name="Recommendation">建议人话描述（动词+结果）。</param>
/// <param name="RuleIds">建议应用的规则 Id（供产品 A 的 applyProfile 使用）。</param>
/// <param name="ExpectedBenefit">预期收益人话（不夸大，PRD 5.3）。</param>
/// <param name="Severity">严重程度。</param>
public sealed record Diagnosis(
    string SymptomKey,
    string Symptom,
    IReadOnlyList<string> Evidence,
    string RecommendationId,
    string Recommendation,
    IReadOnlyList<string> RuleIds,
    string ExpectedBenefit,
    DiagnosisSeverity Severity);

/// <summary>建议稳定键（供最小引擎 / 产品 A 映射，避免魔法字符串散落）。</summary>
public static class RecommendationIds
{
    /// <summary>中断 / 驱动亲和（需产品 A）。</summary>
    public const string InterruptAffinity = "rec.interrupt.affinity";

    /// <summary>内存清理（最小引擎可独立完成）。</summary>
    public const string MemoryCleanup = "rec.memory.standby";

    /// <summary>电源计划 + 计时器（最小引擎可独立完成）。</summary>
    public const string PowerPlan = "rec.power.highperf";

    /// <summary>GPU 瓶颈 / HAGS / MPO（需产品 A）。</summary>
    public const string GpuBottleneck = "rec.gpu.hags.mpo";
}
