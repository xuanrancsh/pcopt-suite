// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Tweak;

/// <summary>
/// 系统适配条件（架构 3.1：OS 版本 + Build + CPU 架构 + GPU 厂商）。
/// 所有字段为 null / 空 = 不限；同规则多条条件之间为 AND 语义（全部满足才应用）。
/// 用法：<c>new OsCondition { MinBuild = 22000 }</c>。
/// </summary>
public sealed record OsCondition
{
    /// <summary>操作系统名称（如 "Windows"）；null = 不限。</summary>
    public string? OsName { get; init; }

    /// <summary>最小 OS Build 号（如 22000 = Win11）；null = 不限。</summary>
    public int? MinBuild { get; init; }

    /// <summary>最大 OS Build 号；null = 不限。</summary>
    public int? MaxBuild { get; init; }

    /// <summary>CPU 架构（X86 / X64 / Arm64）；null = 不限。</summary>
    public string? CpuArchitecture { get; init; }

    /// <summary>GPU 厂商（NVIDIA / AMD / Intel / any）；null 或 any = 不限。</summary>
    public string? GpuVendor { get; init; }
}
