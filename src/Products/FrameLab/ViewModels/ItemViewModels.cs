// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>诊断行视图模型（T35）：症状 + 证据 + 建议 + 预期收益（人话）。</summary>
public sealed class DiagnosisRowViewModel
{
    /// <summary>初始化诊断行。</summary>
    /// <param name="model">诊断结论。</param>
    public DiagnosisRowViewModel(Diagnosis model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        SeverityText = model.Severity switch
        {
            DiagnosisSeverity.Danger => "🔴 需要处理",
            DiagnosisSeverity.Notice => "🟠 建议处理",
            _ => "🔵 可选",
        };
    }

    /// <summary>底层诊断结论（供应用优化时使用）。</summary>
    public Diagnosis Model { get; }

    /// <summary>症状人话。</summary>
    public string Symptom => Model.Symptom;

    /// <summary>建议人话。</summary>
    public string Recommendation => Model.Recommendation;

    /// <summary>预期收益人话。</summary>
    public string ExpectedBenefit => Model.ExpectedBenefit;

    /// <summary>证据（合并为一段，人话）。</summary>
    public string EvidenceText => string.Join("；", Model.Evidence);

    /// <summary>严重程度人话标签。</summary>
    public string SeverityText { get; }
}

/// <summary>历史记录行视图模型（C-P1-3）。</summary>
public sealed class HistoryRowViewModel
{
    /// <summary>初始化历史行。</summary>
    /// <param name="entry">历史条目。</param>
    public HistoryRowViewModel(CaptureHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        TimeText = entry.CreatedAtLocal.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        ProcessText = string.IsNullOrWhiteSpace(entry.ProcessName) ? "（未指定进程）" : entry.ProcessName;
        ScoreText = $"平均 {entry.AverageFps.ToString("F0", CultureInfo.InvariantCulture)} FPS · 1% Low {entry.LowOnePercentFps.ToString("F0", CultureInfo.InvariantCulture)}";
        Label = string.IsNullOrWhiteSpace(entry.Label) ? "测试" : entry.Label;
    }

    /// <summary>时间（本地）。</summary>
    public string TimeText { get; }

    /// <summary>目标进程。</summary>
    public string ProcessText { get; }

    /// <summary>成绩摘要。</summary>
    public string ScoreText { get; }

    /// <summary>标签（优化前 / 优化后 / 测试）。</summary>
    public string Label { get; }
}
