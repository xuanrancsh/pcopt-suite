// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 诊断引擎（T35，PRD C-P0-4）：把"症状"匹配到"建议 + 证据 + 预期收益（人话）"。
/// <para>
/// 覆盖四类症状：帧时间尖峰 + DPC 高（驱动 / 中断亲和）、待机列表膨胀（内存清理）、
/// CPU 降频（电源计划）、GPU 瓶颈（HAGS / MPO）。纯函数、不触碰系统、不抛异常。
/// </para>
/// </summary>
public static class Diagnoser
{
    /// <summary>运行诊断，返回命中的诊断项（按严重程度降序、稳定键升序）。</summary>
    /// <param name="input">诊断输入。</param>
    public static IReadOnlyList<Diagnosis> Diagnose(DiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var results = new List<Diagnosis>();

        AddFrameSpike(input, results);
        AddStandbyBloat(input, results);
        AddCpuThrottling(input, results);
        AddGpuBottleneck(input, results);

        return results
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.SymptomKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>帧时间尖峰 + DPC 延迟偏高 → 驱动 / 中断亲和建议。</summary>
    private static void AddFrameSpike(DiagnosisInput input, List<Diagnosis> results)
    {
        var stats = input.Stats;
        if (stats.StutterCount <= 0 || !stats.Dpc.IsElevated)
        {
            return;
        }

        var severity = stats.StuttersPerMinute >= 6 || stats.Dpc.MaxMs >= 10
            ? DiagnosisSeverity.Danger
            : DiagnosisSeverity.Notice;

        results.Add(new Diagnosis(
            "symptom.frame_spike_dpc",
            "游戏偶尔卡一下，且系统响应有延迟感",
            new[]
            {
                $"采集的 {stats.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture)} 秒内出现 {stats.StutterCount} 次卡顿（单帧超过 {Ms(stats.StutterThresholdMs)}，约为平常的两倍）",
                $"系统调度 / 中断延迟：平均 {Ms(stats.Dpc.AverageMs)}，最高 {Ms(stats.Dpc.MaxMs)}",
                "帧时间尖峰与系统响应延迟同时出现，通常指向驱动或中断处理抢占了 CPU",
            },
            RecommendationIds.InterruptAffinity,
            "调整显卡驱动与中断处理优先级，让画面不再被后台中断打断",
            new[] { "latency.interrupt.affinity", "latency.driver.dpc.priority" },
            "可能减少帧时间尖峰、让 1% Low 更稳；效果因驱动与主板而异。",
            severity));
    }

    /// <summary>待机内存膨胀 → 内存清理建议。</summary>
    private static void AddStandbyBloat(DiagnosisInput input, List<Diagnosis> results)
    {
        if (!input.StandbyListExpanded)
        {
            return;
        }

        results.Add(new Diagnosis(
            "symptom.standby_bloat",
            "系统长时间未重启，内存被后台缓存，游戏偶发卡顿",
            new[]
            {
                "待机内存（Standby List）已膨胀，占用大量物理内存",
                "内存吃紧时系统会边玩边回收缓存，造成瞬时卡顿",
            },
            RecommendationIds.MemoryCleanup,
            "清理待机内存（把不常用的缓存交还系统），立刻腾出可用内存",
            new[] { "mem.standby.purge", "mem.workingset.trim" },
            "通常能立即回收数百 MB 到数 GB 内存，缓解内存不足导致的卡顿。",
            DiagnosisSeverity.Notice));
    }

    /// <summary>CPU 降频 → 电源计划建议。</summary>
    private static void AddCpuThrottling(DiagnosisInput input, List<Diagnosis> results)
    {
        if (!input.CpuThrottling)
        {
            return;
        }

        results.Add(new Diagnosis(
            "symptom.cpu_throttle",
            "游戏时 CPU 频率没跑满，画面偶有顿挫",
            new[]
            {
                "监控到 CPU 频率被压低（节能策略限制了睿频）",
                "CPU 未跑满会直接拉低平均帧与 1% Low",
            },
            RecommendationIds.PowerPlan,
            "切换到高性能电源计划并锁定更精确的计时器，让 CPU 持续保持高频率",
            new[] { "power.highperf.activate", "latency.timer.0.5ms" },
            "可能让 CPU 维持更高频率，改善平均帧与稳定性；发热与耗电会略升。",
            DiagnosisSeverity.Notice));
    }

    /// <summary>GPU 瓶颈 / HAGS 未开 → HAGS / MPO 建议。</summary>
    private static void AddGpuBottleneck(DiagnosisInput input, List<Diagnosis> results)
    {
        if (!input.GpuBottleneck && input.HagsEnabled)
        {
            return;
        }

        var evidence = new List<string>();
        if (input.GpuBottleneck)
        {
            evidence.Add("帧时间长期贴着 GPU 忙时，说明瓶颈在显卡");
        }

        if (!input.HagsEnabled)
        {
            evidence.Add("硬件加速 GPU 计划（HAGS）未开启，调度稍有延迟");
        }

        if (input.MpoEnabled)
        {
            evidence.Add("多平面叠加（MPO）已开启，部分驱动组合下会引发偶发卡顿");
        }

        if (evidence.Count == 0)
        {
            return;
        }

        // 合规：不做任何显卡型号伪装，只做真实的开关/调度调整（PRD 红线 #1）
        results.Add(new Diagnosis(
            "symptom.gpu_bottleneck",
            "显卡是当前的性能瓶颈",
            evidence,
            RecommendationIds.GpuBottleneck,
            "开启硬件加速 GPU 计划并按需修复 MPO，让显卡调度更顺；同时适度降低吃显卡的画质项",
            new[] { "gpu.hags.enable", "latency.dwm.mpo.disable" },
            "可能改善显卡调度与偶发卡顿；对纯算力瓶颈帮助有限，效果因显卡与驱动而异。",
            input.GpuBottleneck ? DiagnosisSeverity.Notice : DiagnosisSeverity.Info));
    }

    private static string Ms(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture) + " ms";
}
