// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows.Media.Imaging;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// PNG 导出「有效性」独立验证（QA 构造，T37 重点 2）：不仅查文件头，
/// 还用 WPF 解码器<b>真实解码</b>，核对尺寸合理、可解码、非空。
/// <para>
/// 渲染统一经 <see cref="StaRunner"/> 的常驻 STA 线程完成，故不依赖任何"冻结静态画刷"的 workaround，
/// 从而保持源码缺陷 D1（静态画刷未 Freeze）可被独立断言暴露（见 KnownDefectReproTests）。
/// </para>
/// </summary>
public sealed class ReportPngDecodeTests
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static IReadOnlyList<FrameSample> Samples(int count = 300)
    {
        var list = new List<FrameSample>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new FrameSample
            {
                Application = "game.exe",
                TimeInSeconds = i / 60.0,
                FrameTimeMs = i % 97 == 0 ? 80.0 : 16.6, // 周期性尖峰
                CpuMs = 2.0,
                GpuMs = 15.0,
                DisplayLatencyMs = 20.0,
            });
        }

        return list;
    }

    private static FrameStats Stats(IReadOnlyList<FrameSample> samples) =>
        FrameAnalyzer.Analyze(samples, new DpcStats(1.2, 6.0, samples.Count));

    [Fact]
    public void ExportPng_DefaultSize_DecodesToReasonableDimensions()
    {
        var samples = Samples();
        var path = NewTemp(".png");
        try
        {
            Result<string>? result = null;
            StaRunner.Run(() =>
            {
                result = new ReportExporter().ExportPng(Stats(samples), samples, path);
            });

            result!.IsSuccess.Should().BeTrue(result.Error);
            File.Exists(path).Should().BeTrue();

            var bytes = File.ReadAllBytes(path);
            bytes.Take(8).Should().Equal(PngSignature);
            new FileInfo(path).Length.Should().BeGreaterThan(2_000, "报告图应包含实际绘制的像素，而非空白");

            var (width, height) = Decode(path);
            width.Should().Be(900);
            height.Should().Be(480);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void ExportPng_TinySamples_StillProducesDecodableImage()
    {
        var samples = Samples().Take(1).ToArray(); // 触发"暂无帧时间数据"分支
        var path = NewTemp(".png");
        try
        {
            Result<string>? result = null;
            StaRunner.Run(() =>
            {
                result = new ReportExporter().ExportPng(Stats(samples), samples, path);
            });

            result!.IsSuccess.Should().BeTrue(result.Error);
            var (width, height) = Decode(path);
            width.Should().Be(900);
            height.Should().Be(480);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Render_CustomSize_ProducesBitmapOfRequestedSize()
    {
        var samples = Samples();
        var stats = Stats(samples);

        BitmapSource? bitmap = null;
        StaRunner.Run(() =>
        {
            bitmap = ReportPngRenderer.Render(stats, samples, width: 640, height: 360, title: "自定义尺寸报告");
        });

        bitmap.Should().NotBeNull();
        bitmap!.PixelWidth.Should().Be(640);
        bitmap.PixelHeight.Should().Be(360);
        bitmap.IsFrozen.Should().BeTrue("渲染结果应冻结以支持跨线程使用");
    }

    private static (int Width, int Height) Decode(string path)
    {
        var width = 0;
        var height = 0;
        StaRunner.Run(() =>
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            decoder.Frames.Count.Should().BeGreaterThan(0, "PNG 应至少包含一帧");
            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
        });

        return (width, height);
    }

    private static string NewTemp(string extension) =>
        Path.Combine(Path.GetTempPath(), "framelab-png-" + Guid.NewGuid().ToString("N") + extension);

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
