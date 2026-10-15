// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>PresentMon 子进程采集器测试（T32）：缺失时给明确中文错误、不崩溃；命令行契约正确。</summary>
public sealed class PresentMonRunnerTests
{
    [Fact]
    public void Locate_WhenExecutableMissing_ReportsUnavailableWithHowToObtain()
    {
        var runner = new ProcessPresentMonRunner(NullLogger<ProcessPresentMonRunner>.Instance, () => null);

        var availability = runner.Locate();

        availability.Available.Should().BeFalse();
        availability.ExecutablePath.Should().BeNull();
        availability.Message.Should().Contain("PresentMon");
        availability.Message.Should().Contain("fetch-presentmon");
    }

    [Fact]
    public void Locate_WhenPathDoesNotExist_ReportsUnavailable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "framelab-no-such-" + Guid.NewGuid().ToString("N"), "PresentMon.exe");
        var runner = new ProcessPresentMonRunner(NullLogger<ProcessPresentMonRunner>.Instance, () => missing);

        runner.Locate().Available.Should().BeFalse();
    }

    [Fact]
    public void Capture_WhenMissing_ReturnsClearChineseError_DoesNotCrash()
    {
        var runner = new ProcessPresentMonRunner(NullLogger<ProcessPresentMonRunner>.Instance, () => null);
        var options = new PresentMonOptions("game.exe", 5, Path.Combine(Path.GetTempPath(), "x.csv"));

        var result = runner.Capture(options, progress: null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("PresentMon");
    }

    [Fact]
    public void Capture_NonPositiveDuration_Fails()
    {
        var runner = new ProcessPresentMonRunner(NullLogger<ProcessPresentMonRunner>.Instance, () => null);
        var result = runner.Capture(new PresentMonOptions("g", 0, "x.csv"), null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BuildArguments_IncludesProcessNameOutputAndTimed()
    {
        var args = ProcessPresentMonRunner.BuildArguments(new PresentMonOptions("cs2.exe", 60, @"C:\tmp\a.csv"));

        args.Should().Contain("--process_name \"cs2.exe\"");
        args.Should().Contain("--output_file ");
        args.Should().Contain("a.csv");
        args.Should().Contain("--timed 60");
    }

    [Fact]
    public void BuildArguments_EmptyProcessName_OmitsProcessName()
    {
        var args = ProcessPresentMonRunner.BuildArguments(new PresentMonOptions(string.Empty, 30, "b.csv"));

        args.Should().NotContain("--process_name");
        args.Should().Contain("--timed 30");
    }
}
