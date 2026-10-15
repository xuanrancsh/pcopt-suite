// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Reflection;
using System.Windows.Media;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// 源码缺陷复现 / 修复验证（QA 构造，T37）。
/// <para>
/// 命名约定：本文件每个用例断言的是<b>期望（正确）行为</b>，因此对当前未修复源码应<b>失败</b>，
/// 用来客观坐实缺陷；待工程师修复后应自动转为<b>通过</b>，作为 R2 的修复验证用例。
/// 均以 <c>Trait("Category","KnownDefect")</c> 标记，可单独筛选：<c>--filter Category=KnownDefect</c>。
/// </para>
/// 已识别缺陷：
/// <list type="bullet">
///   <item>D1：<see cref="ReportPngRenderer"/> 8 个静态 WPF 画刷未 Freeze → 跨 STA 线程使用抛
///     “无法使用属于与父 Freezable 不同的线程的 DependencyObject”。</item>
///   <item>D2：<c>Diagnoser.AddFrameSpike</c> 证据文案把时长写死为「60 秒内」，未使用
///     <see cref="FrameStats.DurationSeconds"/>，采集时长非 60 秒时误导读数。</item>
///   <item>D3：<see cref="ReportPngRenderer.Render"/> 把 <paramref name="height"/> 下限钳到 200，
///     但绘制区上边距 176 + 下边距 60 = 236；当 height ∈ [200,235] 时
///     <c>new Rect(28,176,width-56,height-236)</c> 高度为负 → ArgumentException 崩溃。</item>
/// </list>
/// </summary>
[Trait("Category", "KnownDefect")]
public sealed class KnownDefectReproTests
{
    // ---------------------------------------------------------------------
    // D1：静态画刷必须已 Freeze（否则不可跨 STA 线程安全复用）
    // ---------------------------------------------------------------------
    [Fact]
    public void D1_ReportPngRenderer_StaticBrushes_MustBeFrozen()
    {
        var unfrozen = new List<string>();

        // 在常驻 STA 线程内读取静态字段（触发其初始化），保证与所有渲染同线程，
        // 既不掩盖缺陷，也不制造跨线程副作用。
        StaRunner.Run(() =>
        {
            foreach (var field in typeof(ReportPngRenderer).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (field.GetValue(null) is Brush brush && !brush.IsFrozen)
                {
                    unfrozen.Add(field.Name);
                }
            }
        });

        unfrozen.Should().BeEmpty(
            "ReportPngRenderer 的静态 WPF 画刷必须 Freeze，才能在其文档允诺的 STA 工作线程间安全复用（缺陷 D1：{0}）",
            string.Join(", ", unfrozen));
    }

    // ---------------------------------------------------------------------
    // D3：height 处于钳位下限区间时不得崩溃
    // ---------------------------------------------------------------------
    [Theory]
    [InlineData(200)]
    [InlineData(220)]
    [InlineData(235)]
    [InlineData(236)]
    [InlineData(300)]
    [InlineData(480)]
    public void D3_ReportPngRenderer_HeightAtClampFloor_DoesNotThrow(double height)
    {
        Exception? captured = null;
        StaRunner.Run(() =>
        {
            try
            {
                var img = ReportPngRenderer.Render(FrameStats.Empty, Array.Empty<FrameSample>(), width: 900, height: height);
                img.PixelHeight.Should().Be((int)Math.Ceiling(height));
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        captured.Should().BeNull(
            "height={0} 在 Render 的钳位下限(≥200)与布局下边距(236)之间不得抛异常（缺陷 D3：Rect 高度为负）",
            height);
    }

    // ---------------------------------------------------------------------
    // D2：帧尖峰证据文案必须反映真实采集时长，而非写死 60 秒
    // ---------------------------------------------------------------------
    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(120)]
    public void D2_Diagnoser_FrameSpikeEvidence_ReflectsActualDuration(double durationSeconds)
    {
        var stats = new FrameStats
        {
            FrameCount = 100,
            DurationSeconds = durationSeconds,
            AverageFps = 60,
            MedianFrameTimeMs = 10,
            StutterThresholdMs = 20,
            Stutters = Enumerable.Range(0, 2).Select(i => new StutterEvent(i, i, 30, 3)).ToArray(),
            Dpc = new DpcStats(1.5, 8.0, 100),
        };
        var input = new DiagnosisInput { Stats = stats, HagsEnabled = true, MpoEnabled = false };

        var evidence = Diagnoser.Diagnose(input)
            .Single(d => d.SymptomKey == "symptom.frame_spike_dpc")
            .Evidence;
        var text = string.Join(" || ", evidence);

        text.Should().NotContain("60 秒内",
            "采集时长为 {0} 秒，证据文案不应写死「60 秒内」（缺陷 D2）", durationSeconds);
        text.Should().Contain(durationSeconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " 秒",
            "证据文案应体现真实采集时长 {0} 秒（缺陷 D2）", durationSeconds);
    }
}
