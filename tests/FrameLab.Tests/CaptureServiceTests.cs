// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>采集编排测试（T32/T33）：合成数据产出会话；采集器缺失时明确失败、不崩溃。</summary>
public sealed class CaptureServiceTests
{
    private static IReadOnlyList<FrameSample> SyntheticSamples() =>
        Enumerable.Range(0, 100)
            .Select(i => new FrameSample { FrameTimeMs = 16.0, TimeInSeconds = i * 0.016, Application = "game.exe" })
            .ToArray();

    [Fact]
    public void Availability_ReflectsRunner()
    {
        var available = new CaptureService(new FakePresentMonRunner(true), new FakeDpcSampler(), NullLogger<CaptureService>.Instance);
        available.Availability.Available.Should().BeTrue();

        var missing = new CaptureService(new FakePresentMonRunner(false), new FakeDpcSampler(), NullLogger<CaptureService>.Instance);
        missing.Availability.Available.Should().BeFalse();
        missing.Availability.Message.Should().Contain("PresentMon");
    }

    [Fact]
    public void RunCapture_WhenPresentMonMissing_ReturnsClearError_DoesNotInvokeCapture()
    {
        var runner = new FakePresentMonRunner(false);
        var service = new CaptureService(runner, new FakeDpcSampler(), NullLogger<CaptureService>.Instance);

        var result = service.RunCapture("game.exe", 60, "本次测试");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PresentMon");
        runner.CaptureCount.Should().Be(0);
    }

    [Fact]
    public void RunCapture_NonPositiveDuration_Fails()
    {
        var service = new CaptureService(new FakePresentMonRunner(true), new FakeDpcSampler(), NullLogger<CaptureService>.Instance);
        service.RunCapture("game.exe", 0, "本次测试").IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// 采集编排 + DPC 统计传播的<b>确定性</b>用例（T38/CI 稳健化）。
    /// <para>
    /// 原写法依赖产品内 <c>Task.Run</c> 的 DPC 采样委托能在 2s 有界等待内被线程池调度——
    /// 在 2 核慢速 CI VM 上，重负载下线程池可能延迟注入线程，导致委托"尚未开始"、
    /// 有界等待超时 → DPC 退化为空 → 断言失败（属环境调度，非产品缺陷）。
    /// </para>
    /// <para>
    /// 现改为与被测行为<b>等价</b>的确定性同步（参考 QA 在 <c>DpcRaceStressTests</c> 建立的
    /// 「门控 + 信号」手法）：采集替身在返回前<b>等待 DPC 采样委托确实完成</b>（宽裕上限），
    /// 使其后的有界等待必然命中"已完成"的任务。断言<b>不弱化</b>——仍严格断言 <c>Stats.Dpc == dpc</c>。
    /// </para>
    /// </summary>
    [Fact]
    public void RunCapture_WithSyntheticSamples_AnalyzesAndReturnsSession()
    {
        var samples = SyntheticSamples();
        var dpc = new DpcStats(1.5, 8.0, 50);
        using var dpcSamplerCompleted = new ManualResetEventSlim(false);
        var sampler = new CompletingSignalingSampler(new FakeDpcSampler(dpc), dpcSamplerCompleted);
        var runner = new DpcGatedRunner(samples, dpcSamplerCompleted);
        var service = new CaptureService(runner, sampler, NullLogger<CaptureService>.Instance);
        var progress = new CollectingProgress<FrameSample>();

        var result = service.RunCapture("game.exe", 60, "本次测试", progress);

        result.IsSuccess.Should().BeTrue();
        var session = result.Value!;
        session.Samples.Should().HaveCount(100);
        session.Stats.FrameCount.Should().Be(100);
        session.Stats.AverageFps.Should().BeApproximately(62.5, 1e-9);
        session.Stats.Dpc.Should().Be(dpc);
        session.Label.Should().Be("本次测试");
        session.ProcessName.Should().Be("game.exe");
        progress.Items.Should().HaveCount(100);
        runner.CaptureCount.Should().Be(1);
    }

    /// <summary>
    /// 采集替身：返回合成样本，但在返回前<b>等待 DPC 采样委托确实完成</b>（宽裕上限，仅兜底）。
    /// 由此，产品在采集结束后进行的有界等待必然命中"已完成"的 DPC 任务，
    /// 使 DPC 统计的传播与线程池调度速度无关。
    /// </summary>
    private sealed class DpcGatedRunner : IPresentMonRunner
    {
        private readonly IReadOnlyList<FrameSample> _samples;
        private readonly ManualResetEventSlim _dpcSamplerCompleted;

        public DpcGatedRunner(IReadOnlyList<FrameSample> samples, ManualResetEventSlim dpcSamplerCompleted)
        {
            _samples = samples;
            _dpcSamplerCompleted = dpcSamplerCompleted;
        }

        /// <summary>Capture 被调用的次数。</summary>
        public int CaptureCount { get; private set; }

        /// <inheritdoc/>
        public PresentMonAvailability Locate() => new(true, @"C:\fake\PresentMon.exe", "可用");

        /// <inheritdoc/>
        public Result<PresentMonCaptureResult> Capture(
            PresentMonOptions options,
            IProgress<FrameSample>? progress,
            CancellationToken cancellationToken)
        {
            CaptureCount++;
            foreach (var sample in _samples)
            {
                progress?.Report(sample);
            }

            // 与调度速度无关：DPC 采样委托一旦完成即被放行；仅在极端异常时才由上限兜底。
            _dpcSamplerCompleted.Wait(TimeSpan.FromSeconds(30));

            return Result<PresentMonCaptureResult>.Ok(new PresentMonCaptureResult(
                _samples,
                options.OutputCsvPath,
                TimeSpan.FromSeconds(Math.Max(1, options.DurationSeconds)),
                string.Empty));
        }
    }

    /// <summary>采样替身装饰器：委托内层采样器，并在<b>其完成后</b>发出信号（finally 保证必达）。</summary>
    private sealed class CompletingSignalingSampler : IDpcSampler
    {
        private readonly IDpcSampler _inner;
        private readonly ManualResetEventSlim _completed;

        public CompletingSignalingSampler(IDpcSampler inner, ManualResetEventSlim completed)
        {
            _inner = inner;
            _completed = completed;
        }

        /// <inheritdoc/>
        public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
        {
            try
            {
                return _inner.Sample(duration, cancellationToken);
            }
            finally
            {
                _completed.Set();
            }
        }
    }
}
