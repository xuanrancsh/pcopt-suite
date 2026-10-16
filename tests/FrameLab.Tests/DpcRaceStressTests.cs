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
/// <b>CI 稳健化原则（T38 初版 → T40 修正）</b>：断言只依赖<b>与线程池调度速度无关</b>的事实。
/// 关键教训：<c>task.Wait(timeout)</c> 只会阻塞调用线程，<b>不会</b>帮助已入队的委托拿到线程池线程；
/// 在 2 vCPU / <c>DOTNET_PROCESSOR_COUNT=1</c> 且线程池被同进程其它测试占满时，委托可能在有界等待
/// 窗口内<b>根本未被调度</b>。故任何"委托必须已被及时调度/执行"的<b>即时</b>断言都是脆弱不变量，必须删除；
/// 一切需要调度的事件一律改成<b>宽裕上限轮询</b>或<b>不可释放门控</b>：
/// <list type="bullet">
///   <item>竞态守护：<c>PartialDpcSampler_InstantCapture_DelegateAlwaysInvoked</c> —— 瞬时采集后
///     在<b>宽裕上限（60s）内轮询</b>等待全部委托被执行；旧竞态（令牌透传 Task.Run）下委托被取消而
///     永不执行 → 计数不足 → 失败（已用临时构造缺陷验证，随后 100% 回退）。</item>
///   <item>时序无关的"不丢弃"证明：<c>RealSampler_WithinWindow_DpcNeverEmpty</c> —— 用<b>不可释放、
///     宽裕上限</b>的门控同步点，保证真实采样委托已在采集窗口内开始执行，再断言其统计被完整保留。</item>
///   <item>有界等待退空：<c>SlowIgnoringSampler_BoundedWait_…</c> —— 门控保证慢采样器不可能在窗口内完成，
///     断言仅保留调度无关量（采集已返回、未等待其结束、退化为空统计、有界耗时）。</item>
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
    /// 门控采集替身：先<b>等待 DPC 采样委托确实开始执行</b>（宽裕上限，避免永久阻塞），
    /// 再让采集结束。用于在"采集窗口内"确定性地验证 DPC 统计不被丢弃。
    /// </summary>
    private sealed class GatedRunner : IPresentMonRunner
    {
        private readonly ManualResetEventSlim _samplerStarted;
        private readonly int _lingerMs;
        private readonly TimeSpan _startWait;
        private readonly IReadOnlyList<FrameSample> _samples = MakeSamples();

        public GatedRunner(ManualResetEventSlim samplerStarted, int lingerMs, TimeSpan startWait)
        {
            _samplerStarted = samplerStarted;
            _lingerMs = lingerMs;
            _startWait = startWait;
        }

        public PresentMonAvailability Locate() => new(true, @"C:\fake\PresentMon.exe", "可用");

        public Result<PresentMonCaptureResult> Capture(PresentMonOptions options, IProgress<FrameSample>? progress, CancellationToken cancellationToken)
        {
            // 宽裕上限轮询：只要委托被执行，此处必然被放行；仅在线程池被长时间饥饿时才超时。
            _samplerStarted.Wait(_startWait);
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
    /// 门控慢采样器：进入后阻塞（无视取消），直到测试在 <c>RunCapture</c> 返回后调用 <see cref="Release"/>。
    /// <para>
    /// 这使"采样器不可能在采集窗口内完成"成为<b>确定性事实</b>（与机器调度速度、<c>Thread.Sleep</c> 精度无关），
    /// 从而确定性地验证有界等待的退空行为。根因：旧的 <c>Thread.Sleep(3000)</c> 在慢环境下，等待线程可能被饿到
    /// 3s 之后才观察到 2s 超时，此刻慢采样器早已完成 → 得到非空统计 → 误判。
    /// </para>
    /// <para>
    /// <b>故意不用 <c>IDisposable</c>/<c>ManualResetEventSlim</c> 作为门</b>：在极端饥饿下，DPC 委托可能在
    /// <b>测试方法返回之后</b>才被调度；若门已被 Dispose，采样器线程上的 <c>Wait</c> 会抛
    /// <see cref="ObjectDisposedException"/> → 变成故障任务（未观察异常地雷）。改为"永不释放的 volatile 标志
    /// + 有界自旋"，迟到被调度的委托也能安全通过、不会抛异常。
    /// </para>
    /// </summary>
    private sealed class GatedSlowSampler : IDpcSampler
    {
        private volatile bool _released;
        private int _entered;
        private int _completed;

        /// <summary>采样委托是否已被调度执行（仅作诊断，<b>不得</b>用于断言）。</summary>
        public bool Entered => Volatile.Read(ref _entered) != 0;

        /// <summary>采样委托是否已返回（<see cref="Release"/> 之后才会置位）。</summary>
        public bool Completed => Volatile.Read(ref _completed) != 0;

        /// <summary>放行门控。测试收尾时调用（尽力而为，不依赖采样器已被调度）。</summary>
        public void Release() => _released = true;

        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _entered, 1);

            // 不可释放门控：有界自旋等待放行（无 IDisposable，迟到调度也不会抛异常）。
            var spin = Stopwatch.StartNew();
            while (!_released && spin.Elapsed < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(5);
            }

            Interlocked.Exchange(ref _completed, 1);
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

        // 在宽裕上限（60s）内轮询等待"全部委托确实被执行"——这是与机器调度速度无关的竞态守护。
        // 关键：绝不"瞬时断言"，因为在 1-CPU/饥饿线程池下委托可能就是调度得很慢。
        var deadline = Stopwatch.StartNew();
        while (sampler.InvokedCount < runs && deadline.Elapsed < TimeSpan.FromSeconds(60))
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
            // 不 Dispose：极端饥饿下委托可能在测试方法返回之后才 set；已释放的 event 会抛 ObjectDisposedException。
            var started = new ManualResetEventSlim(false);
            var sampler = new SignalingSampler(new SchedulingJitterDpcSampler(), started);

            // 宽裕上限门控（60s）：在 1-CPU / 饥饿线程池下也几乎必然等到委托被调度，
            // 采集会一直保持到委托开始执行，从而确定性地保证"窗口内执行且统计不被丢弃"。
            var runner = new GatedRunner(started, lingerMs: 50, startWait: TimeSpan.FromSeconds(60));
            var service = new CaptureService(runner, sampler, NullLogger<CaptureService>.Instance);

            var result = service.RunCapture("game.exe", 5, "本次测试");

            result.IsSuccess.Should().BeTrue(result.Error);
            // 采集内部已（宽裕）轮询等到委托开始执行：此处委托必然已被执行、其统计必然被保留。
            sampler.InvokedCount.Should().Be(1, "DPC 采样委托必须被执行");
            result.Value!.Stats.Dpc.Samples.Should().BeGreaterThan(0,
                "采样器已在采集窗口内执行并累积样本，其统计不得被采集瞬时结束丢弃");
        }
    }

    [Fact]
    public void SlowIgnoringSampler_BoundedWait_DoesNotHang_AndFallsBackToEmpty()
    {
        // 采样器无视取消、且在测试放行前绝不返回：有界等待 2s 后必须放行主流程，绝不永久阻塞。
        // 用"不可释放门控"替代 Thread.Sleep —— 采样器在采集期间不可能完成（确定性）。
        //
        // 断言只保留"与线程池调度速度无关"的量。尤其是**绝不**断言 sampler.Entered==true：
        // task.Wait(2000) 不会帮助已入队的委托拿到线程池线程，饥饿时它可能整个窗口内都没被调度。
        var sampler = new GatedSlowSampler();
        var service = new CaptureService(new FixedDelayRunner(0), sampler, NullLogger<CaptureService>.Instance);

        var sw = Stopwatch.StartNew();
        var result = service.RunCapture("game.exe", 1, "本次测试");
        sw.Stop();

        result.IsSuccess.Should().BeTrue(result.Error);

        // (1) 采集已返回，而门控慢采样器不可能已完成 → 有界等待没有、也不会等待"无视取消的慢采样器"真正结束。
        sampler.Completed.Should().BeFalse("有界等待超窗后必须放行主流程，绝不等待慢采样器真正结束（否则即挂起）");
        // (2) 超窗退化为空统计（门控保证采样器不可能在窗口内完成）。
        result.Value!.Stats.Dpc.Samples.Should().Be(0, "未能在有界窗口内返回时退化为空统计（不阻塞）");
        // (3) 有界等待绝非无界：下界证明确实等待了有界上限（任务未完成时 task.Wait(2000) 必阻塞满 2s），
        //     上界为"绝不挂起"兜底。两项均与线程池调度无关。
        sw.Elapsed.TotalSeconds.Should().BeGreaterThan(1.5, "应确实等待了有界上限，而非提前放弃");
        sw.Elapsed.TotalSeconds.Should().BeLessThan(30, "有界等待绝不退化为无界等待/挂起");

        // 收尾（尽力而为、有界、**不断言**）：放行门控；迟到被调度的委托也能立即通过，不会抛异常。
        sampler.Release();
        var deadline = Stopwatch.StartNew();
        while (!sampler.Completed && deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(10);
        }
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
