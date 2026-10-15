// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// DPC 采集「竞态修复真实性」独立压力证伪（QA 构造，T37 重点 5；T38 CI 稳健化）。
/// <para>
/// 原始竞态：旧实现向 <c>Task.Run</c> 传了取消令牌 —— 采集瞬时结束时（取消早于委托被调度）
/// 委托被判"取消"而<b>完全不执行</b> → DPC 统计丢空。修复：<b>不向 Task.Run 传令牌</b>（委托必然执行）
/// + 有界等待 2s 收尾。
/// </para>
/// <para>
/// <b>CI 稳健化原则（T38）</b>：断言只依赖"委托是否被执行"这一与机器调度速度无关的事实，
/// 绝不依赖"委托能否在某个墙钟窗口内被调度"。原先断言"DPC 恒不为空"在 2 核慢速 CI 上会因
/// 线程池调度延迟（> 2s 有界等待）而偶发失败——那是环境调度，不是残留竞态：
/// <list type="bullet">
///   <item>竞态守护：<c>PartialDpcSampler_InstantCapture_DelegateAlwaysInvoked</c> —— 在宽裕上限内
///     轮询等待全部委托确实被执行；旧竞态下委托会被丢弃 → 计数不足 → 失败。</item>
///   <item>时序无关的"不丢弃"证明：<c>RealSampler_WithinWindow_DpcNeverEmpty</c> —— 用<b>可控同步点</b>
///     保证真实采样委托已在采集窗口内开始执行，再断言其统计被完整保留（确定通过，无墙钟依赖）。</item>
/// </list>
/// </para>
/// </summary>
public sealed class DpcRaceStressTests
{
    private const int DpcWaitBudgetMs = 2_000;

    private static IReadOnlyList<FrameSample> MakeSamples(int count = 20) =>
        Enumerable.Range(0, count)
            .Select(i => new FrameSample { FrameTimeMs = 16.0, TimeInSeconds = i * 0.016 })
            .ToArray();

    /// <summary>采集替身：可选固定延迟；忠于取消语义（预取消即失败返回）。</summary>
    private sealed class FixedDelayRunner : IPresentMonRunner
    {
        private readonly int _delayMs;
        private readonly IReadOnlyList<FrameSample> _samples;

        public FixedDelayRunner(int delayMs, IReadOnlyList<FrameSample>? samples = null)
        {
            _delayMs = delayMs;
            _samples = samples ?? MakeSamples();
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

    /// <summary>
    /// 门控采集替身：先<b>等待 DPC 采样委托确实开始执行</b>（有界上限，避免永久阻塞），
    /// 再让采集结束。用于在"采集窗口内"确定性地验证 DPC 统计不被丢弃，且不依赖机器调度速度。
    /// </summary>
    private sealed class GatedRunner : IPresentMonRunner
    {
        private readonly ManualResetEventSlim _samplerStarted;
        private readonly int _lingerMs;
        private readonly IReadOnlyList<FrameSample> _samples = MakeSamples();

        public GatedRunner(ManualResetEventSlim samplerStarted, int lingerMs)
        {
            _samplerStarted = samplerStarted;
            _lingerMs = lingerMs;
        }

        public PresentMonAvailability Locate() => new(true, @"C:\fake\PresentMon.exe", "可用");

        public Result<PresentMonCaptureResult> Capture(PresentMonOptions options, IProgress<FrameSample>? progress, CancellationToken cancellationToken)
        {
            // 与调度速度无关：只要委托被执行，此处必然被放行；仅在极端异常时才超时。
            _samplerStarted.Wait(TimeSpan.FromSeconds(20));
            if (_lingerMs > 0)
            {
                Thread.Sleep(_lingerMs); // 让采样器累积至少一个样本
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Result<PresentMonCaptureResult>.Fail("采集已取消（测试替身）");
            }

            return Result<PresentMonCaptureResult>.Ok(new PresentMonCaptureResult(_samples, null, TimeSpan.Zero, string.Empty));
        }
    }

    /// <summary>采样替身装饰器：进入即计数并触发"已开始"信号，再委托内层采样器。</summary>
    private sealed class SignalingSampler : IDpcSampler
    {
        private readonly IDpcSampler _inner;
        private readonly ManualResetEventSlim _started;
        private int _invoked;

        public SignalingSampler(IDpcSampler inner, ManualResetEventSlim started)
        {
            _inner = inner;
            _started = started;
        }

        public int InvokedCount => Volatile.Read(ref _invoked);

        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invoked);
            _started.Set();
            return _inner.Sample(duration, cancellationToken);
        }
    }

    /// <summary>忠于产品契约的采样替身：收到取消时"带上已采到的部分返回"（do-while 至少产出 1 个样本）。</summary>
    private sealed class PartialDpcSampler : IDpcSampler
    {
        private int _invoked;

        /// <summary>被调用次数（用于证明"委托未被竞态丢弃"，与调度速度无关）。</summary>
        public int InvokedCount => Volatile.Read(ref _invoked);

        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invoked);
            var count = 0;
            var total = 0.0;
            var max = 0.0;
            do
            {
                Thread.Sleep(2);
                const double overshoot = 0.3;
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

    /// <summary>
    /// 竞态守护（与调度速度无关）：用"瞬时采集 + 忠于契约的采样器"，证明每次采集的 DPC 采样
    /// <b>委托都被执行</b>。旧竞态（向 Task.Run 传令牌）下，取消早于调度会丢弃委托 → 计数不足 → 失败。
    /// </summary>
    [Fact]
    public void PartialDpcSampler_InstantCapture_DelegateAlwaysInvoked()
    {
        var runner = new FixedDelayRunner(0);
        var sampler = new PartialDpcSampler();
        var service = new CaptureService(runner, sampler, NullLogger<CaptureService>.Instance);

        var capturedNonEmpty = 0;
        const int runs = 40;
        for (var i = 0; i < runs; i++)
        {
            var result = service.RunCapture("game.exe", 1, "本次测试");
            result.IsSuccess.Should().BeTrue(result.Error);
            if (result.Value!.Stats.Dpc.Samples > 0)
            {
                capturedNonEmpty++;
            }
        }

        // 在宽裕上限内轮询等待"全部委托确实被执行"——这是与机器调度速度无关的竞态守护。
        var deadline = Stopwatch.StartNew();
        while (sampler.InvokedCount < runs && deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(20);
        }

        sampler.InvokedCount.Should().Be(runs,
            "瞬时采集（最易触发'取消早于调度'）下，每次 DPC 采样委托都必须被执行——"
            + "不得因'不向 Task.Run 传令牌'的修复被回退而丢空");
        capturedNonEmpty.Should().BeGreaterThan(0,
            "采样委托已执行时，其产出的统计不得被整体丢弃（至少可观测到一次非空）");
    }

    /// <summary>
    /// 时序无关的"不丢弃"证明：用可控同步点保证<b>真实采样器</b>已在采集窗口内开始执行，
    /// 再断言其统计被完整保留。确定性通过，不依赖机器调度速度。
    /// </summary>
    [Fact]
    public void RealSampler_WithinWindow_DpcNeverEmpty()
    {
        const int runs = 15;
        for (var i = 0; i < runs; i++)
        {
            using var started = new ManualResetEventSlim(false);
            var sampler = new SignalingSampler(new SchedulingJitterDpcSampler(), started);
            var runner = new GatedRunner(started, lingerMs: 50);
            var service = new CaptureService(runner, sampler, NullLogger<CaptureService>.Instance);

            var result = service.RunCapture("game.exe", 5, "本次测试");

            result.IsSuccess.Should().BeTrue(result.Error);
            sampler.InvokedCount.Should().Be(1, "DPC 采样委托必须被执行");
            result.Value!.Stats.Dpc.Samples.Should().BeGreaterThan(0,
                "采样器已在采集窗口内执行并累积样本，其统计不得被采集瞬时结束丢弃");
        }
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
