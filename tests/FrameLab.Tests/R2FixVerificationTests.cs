// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// R2 修复验证（QA 构造，T37 第 2 轮）。以<b>证伪式、真实行为</b>方式复核工程师对
/// D1/D2/D3/D4 的修复，而非仅看断言变绿：
/// <list type="bullet">
///   <item>D1：在「多个互相独立的 STA 线程」上复用渲染器，验证跨线程不再抛异常（不止断言 IsFrozen）。</item>
///   <item>D2：采集时长逐一变化时证据文案随之变化（30/45/120）。</item>
///   <item>D3：height=200..480 不抛且 <c>PixelHeight</c> 与请求高度一致（见 KnownDefectReproTests）。</item>
///   <item>D4：故障采样器（快故障 + 慢于有界等待的故障）重复执行 ≥30 次的「零未观察任务异常」压力验证，
///     并确认调用方取消语义仍即时返回。</item>
/// </list>
/// </summary>
[Trait("Category", "R2FixVerification")]
public sealed class R2FixVerificationTests
{
    // ================= D1：真实跨 STA 复用 =================

    /// <summary>在「本次新建、与其它线程完全独立」的 STA 线程上执行动作。</summary>
    private static void RunOnFreshSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    private static IReadOnlyList<FrameSample> Samples(int count = 120) =>
        Enumerable.Range(0, count)
            .Select(i => new FrameSample { FrameTimeMs = i % 97 == 0 ? 80.0 : 16.6, TimeInSeconds = i / 60.0 })
            .ToArray();

    [Fact]
    public void D1Fix_StaticBrushesFrozen_AllEight()
    {
        var unfrozen = new List<string>();
        StaRunner.Run(() =>
        {
            foreach (var field in typeof(ReportPngRenderer).GetFields(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
            {
                if (field.GetValue(null) is System.Windows.Media.Brush b && !b.IsFrozen)
                {
                    unfrozen.Add(field.Name);
                }
            }
        });
        unfrozen.Should().BeEmpty("8 个静态画刷都应已 Freeze（D1 修复）");
    }

    [Fact]
    public void D1Fix_MultipleIndependentStaThreads_ReuseDoesNotThrow()
    {
        var stats = FrameAnalyzer.Analyze(Samples(), DpcStats.Empty);

        var errors = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var idx = i;
            try
            {
                // 每轮都是全新且互相独立的 STA 线程：未冻结的画刷会在第 2 轮起抛跨线程异常。
                RunOnFreshSta(() => ReportPngRenderer.Render(stats, Samples(), width: 640, height: 360));
            }
            catch (Exception ex)
            {
                errors.Add($"STA#{idx}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        errors.Should().BeEmpty("冻结后的静态画刷应可跨独立 STA 线程安全复用（D1 真实跨线程验证）");
    }

    // ================= D2：时长逐一变化 =================

    [Theory]
    [InlineData(30, 2)]
    [InlineData(45, 3)]
    [InlineData(120, 5)]
    public void D2Fix_EvidenceText_TracksActualDuration(int durationSeconds, int stutters)
    {
        var stats = new FrameStats
        {
            FrameCount = 200,
            DurationSeconds = durationSeconds,
            AverageFps = 60,
            MedianFrameTimeMs = 10,
            StutterThresholdMs = 20,
            Stutters = Enumerable.Range(0, stutters).Select(i => new StutterEvent(i, i, 30, 3)).ToArray(),
            Dpc = new DpcStats(1.5, 8.0, 100),
        };

        var text = string.Join(" || ",
            Diagnoser.Diagnose(new DiagnosisInput { Stats = stats, HagsEnabled = true, MpoEnabled = false })
                .Single(d => d.SymptomKey == "symptom.frame_spike_dpc")
                .Evidence);

        text.Should().Contain($"{durationSeconds} 秒", "文案应体现真实采集时长");
        text.Should().Contain($"{stutters} 次卡顿", "文案应体现真实卡顿次数");
        text.Should().NotContain("60 秒内", "不得再出现写死的 60 秒");
    }

    // ================= D4：零未观察任务异常压力 =================

    private static int _unobserved;

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Interlocked.Increment(ref _unobserved);
        e.SetObserved();
    }

    /// <summary>采集替身：忠于取消语义（预取消即失败返回），因此可测「调用方取消即时性」。</summary>
    private sealed class FixedDelayRunner : IPresentMonRunner
    {
        public PresentMonAvailability Locate() => new(true, @"C:\fake\PresentMon.exe", "可用");

        public Result<PresentMonCaptureResult> Capture(PresentMonOptions options, IProgress<FrameSample>? progress, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Result<PresentMonCaptureResult>.Fail("采集已取消（测试替身）");
            }

            return Result<PresentMonCaptureResult>.Ok(new PresentMonCaptureResult(Samples(10), null, TimeSpan.Zero, string.Empty));
        }
    }

    /// <summary>故意抛异常的采样器：delayMs 后可快故障或慢于有界等待再故障。</summary>
    private sealed class ThrowingDpcSampler : IDpcSampler
    {
        private readonly int _delayMs;
        public ThrowingDpcSampler(int delayMs) => _delayMs = delayMs;

        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (_delayMs > 0)
            {
                Thread.Sleep(_delayMs);
            }

            throw new InvalidOperationException("采样器故意故障（测试替身）");
        }
    }

    /// <summary>制造一个"引发后即被丢弃"的故障任务（NoInlining 确保局部引用随即失效，可被回收）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FireAndForgetFaultedTask() =>
        _ = Task.Run(() => throw new InvalidOperationException("故意未观察的故障任务（对照）"));

    /// <summary>反复触发 GC / 终结器，直到谓词满足或达到轮次上限（用于观察任务异常）。</summary>
    private static void GcUntil(Func<bool> predicate, int maxRounds = 40)
    {
        for (var i = 0; i < maxRounds && !predicate(); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(20);
        }
    }

    private static void GcRounds(int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(20);
        }
    }

    [Fact]
    public void D4Fix_PositiveControl_UnobservedFaultedTask_IsDetected()
    {
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            Interlocked.Exchange(ref _unobserved, 0);
            FireAndForgetFaultedTask();
            GcUntil(() => Volatile.Read(ref _unobserved) > 0);

            Volatile.Read(ref _unobserved).Should().BeGreaterThan(0,
                "对照项：检测机制本身必须能捕获未观察异常（否则本验证无意义）");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    [Fact]
    public void D4Fix_ThrowingSampler_RepeatedRuns_NoUnobservedTaskException()
    {
        var service = new CaptureService(new FixedDelayRunner(), new ThrowingDpcSampler(0), NullLogger<CaptureService>.Instance);

        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            Interlocked.Exchange(ref _unobserved, 0);

            // 快故障路径：任务在采集成功后有界等待内即故障，异常须被观察。
            for (var i = 0; i < 30; i++)
            {
                var result = service.RunCapture("game.exe", 1, "本次测试");
                result.IsSuccess.Should().BeTrue(result.Error);
                result.Value!.Stats.Dpc.Samples.Should().Be(0, "故障采样器应退化为空统计，不阻断主流程");
            }

            GcRounds(10);
            Volatile.Read(ref _unobserved).Should().Be(0, "重复 30 次故障采样后不得出现未观察任务异常");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    [Fact]
    public void D4Fix_SlowThrowingSampler_OutlivesBoundedWait_NoUnobservedTaskException()
    {
        // 任务慢于 2s 有界等待才故障：走"任务仍在后台运行 → 续延观察异常"分支。
        var service = new CaptureService(new FixedDelayRunner(), new ThrowingDpcSampler(2_200), NullLogger<CaptureService>.Instance);

        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            Interlocked.Exchange(ref _unobserved, 0);

            for (var i = 0; i < 6; i++)
            {
                var result = service.RunCapture("game.exe", 1, "本次测试");
                result.IsSuccess.Should().BeTrue(result.Error);
                result.Value!.Stats.Dpc.Samples.Should().Be(0, "慢于有界等待应退化为空统计");
                Thread.Sleep(500); // 让后台任务完成，触发续延观察
            }

            GcRounds(10);
            Volatile.Read(ref _unobserved).Should().Be(0, "慢故障任务在续延中被观察后，不得出现未观察异常");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    [Fact]
    public void D4Fix_CallerCancellation_StillReturnsImmediately()
    {
        var service = new CaptureService(new FixedDelayRunner(), new ThrowingDpcSampler(3_000), NullLogger<CaptureService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        var result = service.RunCapture("game.exe", 1, "本次测试", progress: null, cts.Token);
        sw.Stop();

        result.IsSuccess.Should().BeFalse("已取消的采集应失败返回");
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(1_500, "调用方取消语义必须即时（不被 DPC 逻辑拖住）");
    }
}
