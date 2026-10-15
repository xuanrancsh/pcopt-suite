// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public void RunCapture_WithSyntheticSamples_AnalyzesAndReturnsSession()
    {
        var samples = SyntheticSamples();
        var dpc = new DpcStats(1.5, 8.0, 50);
        var runner = new FakePresentMonRunner(true, samples);
        var service = new CaptureService(runner, new FakeDpcSampler(dpc), NullLogger<CaptureService>.Instance);
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
}
