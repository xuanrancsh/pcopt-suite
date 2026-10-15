// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>诊断匹配测试（T35 / PRD C-P0-4）：每类症状 → 建议 + 证据 + 预期收益。</summary>
public sealed class DiagnoserTests
{
    private static FrameStats StatsWithStutters(int count, double durationSeconds, DpcStats? dpc = null)
    {
        return new FrameStats
        {
            FrameCount = 100,
            DurationSeconds = durationSeconds,
            AverageFps = 60,
            MedianFrameTimeMs = 10,
            StutterThresholdMs = 20,
            Stutters = Enumerable.Range(0, count).Select(i => new StutterEvent(i, i, 30, 3)).ToArray(),
            Dpc = dpc ?? DpcStats.Empty,
        };
    }

    [Fact]
    public void Diagnose_CleanInput_NoDiagnoses()
    {
        var input = new DiagnosisInput { HagsEnabled = true, MpoEnabled = false };
        Diagnoser.Diagnose(input).Should().BeEmpty();
    }

    [Fact]
    public void Diagnose_FrameSpikeWithHighDpc_MatchesInterruptAffinity()
    {
        var input = new DiagnosisInput
        {
            Stats = StatsWithStutters(3, 60, new DpcStats(1.5, 8.0, 100)),
            HagsEnabled = true,
            MpoEnabled = false,
        };

        var diagnosis = Diagnoser.Diagnose(input).Single();

        diagnosis.SymptomKey.Should().Be("symptom.frame_spike_dpc");
        diagnosis.RecommendationId.Should().Be(RecommendationIds.InterruptAffinity);
        diagnosis.Severity.Should().Be(DiagnosisSeverity.Notice);
        diagnosis.Evidence.Should().NotBeEmpty();
        diagnosis.Recommendation.Should().NotBeNullOrWhiteSpace();
        diagnosis.ExpectedBenefit.Should().NotBeNullOrWhiteSpace();
        diagnosis.RuleIds.Should().NotBeEmpty();
    }

    [Fact]
    public void Diagnose_FrequentStutters_IsDanger()
    {
        // 60 秒内 6 次卡顿 → 6 次/分 → 危险
        var input = new DiagnosisInput
        {
            Stats = StatsWithStutters(6, 60, new DpcStats(1.5, 3.0, 100)),
            HagsEnabled = true,
            MpoEnabled = false,
        };

        Diagnoser.Diagnose(input).Single().Severity.Should().Be(DiagnosisSeverity.Danger);
    }

    [Fact]
    public void Diagnose_VeryHighDpc_IsDanger()
    {
        var input = new DiagnosisInput
        {
            Stats = StatsWithStutters(1, 60, new DpcStats(2.0, 12.0, 100)),
            HagsEnabled = true,
            MpoEnabled = false,
        };

        Diagnoser.Diagnose(input).Single().Severity.Should().Be(DiagnosisSeverity.Danger);
    }

    [Fact]
    public void Diagnose_StutterWithoutElevatedDpc_NoFrameSpikeDiagnosis()
    {
        var input = new DiagnosisInput
        {
            Stats = StatsWithStutters(5, 60, new DpcStats(0.2, 1.0, 100)),
            HagsEnabled = true,
            MpoEnabled = false,
        };

        Diagnoser.Diagnose(input).Should().BeEmpty();
    }

    [Fact]
    public void Diagnose_StandbyBloat_MatchesMemoryCleanup()
    {
        var input = new DiagnosisInput
        {
            Stats = FrameStats.Empty,
            StandbyListExpanded = true,
            HagsEnabled = true,
            MpoEnabled = false,
        };

        var diagnosis = Diagnoser.Diagnose(input).Single();

        diagnosis.SymptomKey.Should().Be("symptom.standby_bloat");
        diagnosis.RecommendationId.Should().Be(RecommendationIds.MemoryCleanup);
        diagnosis.Evidence.Should().NotBeEmpty();
    }

    [Fact]
    public void Diagnose_CpuThrottling_MatchesPowerPlan()
    {
        var input = new DiagnosisInput
        {
            Stats = FrameStats.Empty,
            CpuThrottling = true,
            HagsEnabled = true,
            MpoEnabled = false,
        };

        var diagnosis = Diagnoser.Diagnose(input).Single();

        diagnosis.SymptomKey.Should().Be("symptom.cpu_throttle");
        diagnosis.RecommendationId.Should().Be(RecommendationIds.PowerPlan);
        diagnosis.Evidence.Should().NotBeEmpty();
    }

    [Fact]
    public void Diagnose_GpuBottleneck_MatchesGpuRecommendation()
    {
        var input = new DiagnosisInput
        {
            Stats = FrameStats.Empty,
            GpuBottleneck = true,
            HagsEnabled = true,
            MpoEnabled = false,
        };

        var diagnosis = Diagnoser.Diagnose(input).Single();

        diagnosis.SymptomKey.Should().Be("symptom.gpu_bottleneck");
        diagnosis.RecommendationId.Should().Be(RecommendationIds.GpuBottleneck);
        diagnosis.Severity.Should().Be(DiagnosisSeverity.Notice);
    }

    [Fact]
    public void Diagnose_HagsDisabled_IsInfoAndMentionsHags()
    {
        var input = new DiagnosisInput
        {
            Stats = FrameStats.Empty,
            HagsEnabled = false,
            MpoEnabled = false,
        };

        var diagnosis = Diagnoser.Diagnose(input).Single();

        diagnosis.SymptomKey.Should().Be("symptom.gpu_bottleneck");
        diagnosis.Severity.Should().Be(DiagnosisSeverity.Info);
        diagnosis.Evidence.Should().Contain(e => e.Contains("HAGS"));
    }

    [Fact]
    public void Diagnose_MultipleSymptoms_SortedBySeverityDescending()
    {
        var input = new DiagnosisInput
        {
            Stats = StatsWithStutters(6, 60, new DpcStats(2.0, 12.0, 100)), // Danger
            StandbyListExpanded = true,                                    // Notice
            CpuThrottling = true,                                          // Notice
            GpuBottleneck = true,                                          // Notice
            HagsEnabled = true,
            MpoEnabled = false,
        };

        var results = Diagnoser.Diagnose(input);

        results.Should().HaveCount(4);
        results[0].Severity.Should().Be(DiagnosisSeverity.Danger);
        results.Select(d => d.Severity).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Diagnose_DoesNotSpoofGpuModel_NoVendorFaking()
    {
        // 红线 #1：任何输出都不得包含"伪装显卡型号"式措辞
        var input = new DiagnosisInput
        {
            Stats = FrameStats.Empty,
            GpuBottleneck = true,
            HagsEnabled = false,
            MpoEnabled = true,
            GpuVendor = "NVIDIA",
        };

        var text = string.Join("|", Diagnoser.Diagnose(input).SelectMany(d => d.Evidence.Concat(new[] { d.Recommendation, d.ExpectedBenefit })));
        text.Should().NotContain("伪装");
        text.Should().NotContain("RTX");
    }
}
