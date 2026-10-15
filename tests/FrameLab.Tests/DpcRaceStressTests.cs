// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// DPC 采集「竞态修复真实性」独立压力证伪（QA 构造，T37 重点 5）。
/// 工程师自报：原实现约 1/6 概率 DPC 统计为空（向 Task.Run 传了取消令牌），已修为
/// "不向 Task.Run 传令牌 + 有界等待 2s"。本测试用<b>重复压力 + 真实采样器</b>证伪"DPC 从不为空"，
/// 并验证有界等待不会永久阻塞、也不会拖慢主流程、且不丢失取消语义。
/// </summary>
public sealed class DpcRaceStressTests
{
    private const int DpcWaitBudgetMs = 2_000;

    private sealed class FixedDelayRunner : IPresentMonRunner
    {
        private readonly int _delayMs;
        private readonly IReadOnlyList<FrameSample> _samples;

        public FixedDelayRunner(int delayMs, IReadOnlyList<FrameSample>? samples = null)
        {
            _delayMs = delayMs;
            _samples = samples ?? Enumerable.Range(0, 20)
                .Select(i => new FrameSample { FrameTimeMs = 16.0, TimeInSeconds = i * 0.016 })
                .ToArray();
        }

        public PresentMonAvailability Locate() => new(true, @"C:\fake\PresentMon.exe", "可用");

        public Result<PresentMonCaptureResult> Capture(PresentMonOptions options, IProgress<FrameSample>? progress, CancellationToken cancellationToken)
        {
            if (_delayMs > 0)
            {
                Thread.Sleep(_delayMs);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Result<PresentMonCaptureResult>.Fail("采集已取消（测试替身）");
            }

            foreach (var sample in _samples)
            {
                progress?.Report(sample);
            }

            return Result<PresentMonCaptureResult>.Ok(new PresentMonCaptureResult(_samples, null, TimeSpan.FromMilliseconds(_delayMs), string.Empty));
        }
    }

    /// <summary>忠于产品契约的采样替身：收到取消时"带上已采到的部分返回"（至少采到 1 个样本）。</summary>
    private sealed class PartialDpcSampler : IDpcSampler
    {
        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            var count = 0;
            var total = 0.0;
            var max = 0.0;
            do
            {
                Thread.Sleep(2);
                var overshoot = 0.3;
                total += overshoot;
                max = Math.Max(max, overshoot);
                count++;
            }
            while (!cancellationToken.IsCancellationRequested && count < 50);

            return Result<DpcStats>.Ok(new DpcStats(total / count, max, count));
        }
    }

    /// <summary>故意无视取消、慢速返回的采样替身（验证有界等待：不永久阻塞）。</summary>
    private sealed class SlowIgnoringDpcSampler : IDpcSampler
    {
        private readonly int _sleepMs;

        public SlowIgnoringDpcSampler(int sleepMs) => _sleepMs = sleepMs;

        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            Thread.Sleep(_sleepMs);
            return Result<DpcStats>.Ok(new DpcStats(1.0, 3.0, 10));
        }
    }

    [Fact]
    public void RealSampler_RepeatedRuns_DpcNeverEmpty()
    {
        // 真实调度抖动采样器 + 每次 200ms 的采集时长；重复 15 次，DPC 采样数必须恒 > 0。
        var runner = new FixedDelayRunner(200);
        var service = new CaptureService(runner, new SchedulingJitterDpcSampler(), NullLogger<CaptureService>.Instance);

        // 预热（消除线程池冷启动）
        service.RunCapture("game.exe", 5, "warmup").IsSuccess.Should().BeTrue();

        var emptyCount = 0;
        var maxElapsedMs = 0.0;
        for (var i = 0; i < 15; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = service.RunCapture("game.exe", 5, "本次测试");
            sw.Stop();
            maxElapsedMs = Math.Max(maxElapsedMs, sw.Elapsed.TotalMilliseconds);

            result.IsSuccess.Should().BeTrue(result.Error);
            if (result.Value!.Stats.Dpc.Samples == 0)
            {
                emptyCount++;
            }
        }

        emptyCount.Should().Be(0, "DPC 统计在重复执行中绝不能为空（1/6 竞态应已修复）");
        maxElapsedMs.Should().BeLessThan(DpcWaitBudgetMs + 500,
            "正常路径不应被 DPC 采样拖慢（采集 200ms，远小于 DPC 上限）");
    }

    [Fact]
    public void PartialSampler_InstantCapture_DpcNeverEmpty_Stress40()
    {
        // 采集"瞬时返回"（最易触发原竞态）：不向 Task.Run 传令牌后，委托必然执行，
        // 忠于契约的采样器会带上部分结果返回 → DPC 恒不为空。
        var runner = new FixedDelayRunner(0);
        var service = new CaptureService(runner, new PartialDpcSampler(), NullLogger<CaptureService>.Instance);

        var emptyCount = 0;
        for (var i = 0; i < 40; i++)
        {
            var result = service.RunCapture("game.exe", 1, "本次测试");
            result.IsSuccess.Should().BeTrue(result.Error);
            if (result.Value!.Stats.Dpc.Samples == 0)
            {
                emptyCount++;
            }
        }

        emptyCount.Should().Be(0, "瞬时采集下 DPC 也不应为空（40 次压力）");
    }

    [Fact]
    public void SlowIgnoringSampler_BoundedWait_DoesNotHang_AndFallsBackToEmpty()
    {
        // 采样器无视取消、要 3s 才返回：有界等待 2s 后应放行，绝不永久阻塞。
        var runner = new FixedDelayRunner(0);
        var service = new CaptureService(runner, new SlowIgnoringDpcSampler(3_000), NullLogger<CaptureService>.Instance);

        var sw = Stopwatch.StartNew();
        var result = service.RunCapture("game.exe", 1, "本次测试");
        sw.Stop();

        result.IsSuccess.Should().BeTrue(result.Error);
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(3_500,
            "有界等待应约 2s 后放行，不永久阻塞主流程");
        sw.Elapsed.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(1_500,
            "应确实等待了有界上限，而非提前放弃");
        result.Value!.Stats.Dpc.Samples.Should().Be(0, "未能在有界窗口内返回时退化为空统计（不阻塞）");
    }

    [Fact]
    public void CallerCancellation_IsStillHonored_ReturnsQuickly()
    {
        // 调用方取消语义必须保留：预取消令牌 → 采集立即失败返回，不因 DPC 逻辑被拖住。
        var runner = new FixedDelayRunner(0);
        var service = new CaptureService(runner, new SlowIgnoringDpcSampler(3_000), NullLogger<CaptureService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        var result = service.RunCapture("game.exe", 1, "本次测试", progress: null, cts.Token);
        sw.Stop();

        result.IsSuccess.Should().BeFalse("已取消的采集应失败返回");
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(1_500);
    }
}
