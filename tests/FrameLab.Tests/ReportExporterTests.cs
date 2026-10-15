// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>报告导出测试（T34 / PRD C-P0-3 / C-P0-6）：CSV 内容与 PNG 文件生成。</summary>
public sealed class ReportExporterTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static IReadOnlyList<FrameSample> Samples() =>
        Enumerable.Range(0, 120)
            .Select(i => new FrameSample
            {
                Application = "game.exe",
                TimeInSeconds = i / 60.0,
                FrameTimeMs = i == 60 ? 60.0 : 16.0,
                CpuMs = 2.0,
                GpuMs = 15.0,
                DisplayLatencyMs = 20.0,
            })
            .ToArray();

    private static FrameStats Stats()
    {
        var samples = Samples();
        return FrameAnalyzer.Analyze(samples, new DpcStats(1.2, 6.0, 120));
    }

    [Fact]
    public void ExportCsv_WritesBomAndContent()
    {
        var path = NewTemp(".csv");
        try
        {
            var result = new ReportExporter().ExportCsv(Stats(), Samples(), path);

            result.IsSuccess.Should().BeTrue();
            File.Exists(path).Should().BeTrue();

            var bytes = File.ReadAllBytes(path);
            bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF }); // UTF-8 BOM

            var text = File.ReadAllText(path, Encoding.UTF8);
            text.Should().Contain("平均 FPS");
            text.Should().Contain("1% Low 时间占比法");
            text.Should().Contain("1% Low 统计百分位法");
            text.Should().Contain("Application,TimeInSeconds,FrameTimeMs,CpuMs,GpuMs,DisplayLatencyMs");
            text.Should().Contain("game.exe");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ExportCsv_EmptyPath_Fails()
    {
        new ReportExporter().ExportCsv(Stats(), Samples(), "   ").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ExportComparisonCsv_WritesRows()
    {
        var path = NewTemp(".csv");
        try
        {
            var comparison = ComparisonAnalyzer.Compare(Stats(), Stats());
            var result = new ReportExporter().ExportComparisonCsv(comparison, path);

            result.IsSuccess.Should().BeTrue();
            var text = File.ReadAllText(path, Encoding.UTF8);
            text.Should().Contain("指标,优化前,优化后,变化,结论");
            text.Should().Contain("平均帧率");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ExportPng_GeneratesValidPngFile()
    {
        var path = NewTemp(".png");
        try
        {
            Result<string>? result = null;
            StaRunner.Run(() => result = new ReportExporter().ExportPng(Stats(), Samples(), path));

            result.Should().NotBeNull();
            result!.IsSuccess.Should().BeTrue();
            File.Exists(path).Should().BeTrue();

            var header = new byte[8];
            using (var stream = File.OpenRead(path))
            {
                stream.ReadExactly(header, 0, header.Length);
            }

            header.Should().Equal(PngSignature);
            new FileInfo(path).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ExportPng_EmptyPath_Fails()
    {
        Result<string>? result = null;
        StaRunner.Run(() => result = new ReportExporter().ExportPng(Stats(), Samples(), string.Empty));
        result!.IsSuccess.Should().BeFalse();
    }

    private static string NewTemp(string extension) =>
        Path.Combine(Path.GetTempPath(), "framelab-report-" + Guid.NewGuid().ToString("N") + extension);

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
