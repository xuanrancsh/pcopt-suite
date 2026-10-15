// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>PresentMon CSV 解析健壮性测试（T32 / 架构 8.2）。</summary>
public sealed class PresentMonCsvParserTests
{
    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsEmpty()
    {
        PresentMonCsvParser.Parse(string.Empty).Should().BeEmpty();
        PresentMonCsvParser.Parse("   \r\n\r\n").Should().BeEmpty();
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmpty()
    {
        PresentMonCsvParser.Parse("Application,msBetweenPresents\r\n").Should().BeEmpty();
    }

    [Fact]
    public void Parse_MissingFrameTimeColumn_ReturnsEmpty()
    {
        // 没有帧时间列 → 无法产出有用样本
        PresentMonCsvParser.Parse("Application,msUntilDisplayed\r\ngame.exe,20.1\r\n").Should().BeEmpty();
    }

    [Fact]
    public void Parse_TypicalPresentMonHeader_MapsColumns()
    {
        const string csv =
            "Application,msBetweenPresents,msUntilDisplayed,msInPresentAPI,gpuBusy\r\n" +
            "game.exe,16.700,20.100,2.500,15.000\r\n" +
            "game.exe,16.600,20.000,2.400,14.900\r\n";

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().HaveCount(2);
        samples[0].Application.Should().Be("game.exe");
        samples[0].FrameTimeMs.Should().BeApproximately(16.700, 1e-9);
        samples[0].DisplayLatencyMs.Should().BeApproximately(20.100, 1e-9);
        samples[0].CpuMs.Should().BeApproximately(2.500, 1e-9);
        samples[0].GpuMs.Should().BeApproximately(15.000, 1e-9);
    }

    [Fact]
    public void Parse_CaseAndSpacingInsensitiveAliases_AreMatched()
    {
        const string csv = "Process Name, Frame Time \r\ngame.exe,16.7\r\n";
        var samples = PresentMonCsvParser.Parse(csv);
        samples.Should().ContainSingle();
        samples[0].Application.Should().Be("game.exe");
        samples[0].FrameTimeMs.Should().BeApproximately(16.7, 1e-9);
    }

    [Fact]
    public void Parse_SkipsBadRows_KeepsValidOnes()
    {
        const string csv =
            "Application,msBetweenPresents\r\n" +
            "g,16.7\r\n" +          // 有效
            "g,abc\r\n" +           // 非数字 → 跳过
            "g,16.6\r\n" +          // 有效
            "g,16.7,extra\r\n" +    // 字段数不符 → 跳过
            "g,-5\r\n" +            // 非正 → 跳过
            "g,\r\n";               // 空 → 跳过

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().HaveCount(2);
        samples[0].FrameTimeMs.Should().BeApproximately(16.7, 1e-9);
        samples[1].FrameTimeMs.Should().BeApproximately(16.6, 1e-9);
    }

    [Fact]
    public void Parse_NonPositiveAndNonFiniteFrameTime_AreSkipped()
    {
        const string csv =
            "Application,msBetweenPresents\r\n" +
            "g,NaN\r\n" +
            "g,Infinity\r\n" +
            "g,0\r\n" +
            "g,16.7\r\n";

        PresentMonCsvParser.Parse(csv).Should().ContainSingle();
    }

    [Fact]
    public void Parse_QuotedFieldWithComma_IsParsedAsOneField()
    {
        const string csv =
            "Application,msBetweenPresents\r\n" +
            "\"game, the best.exe\",16.7\r\n";

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().ContainSingle();
        samples[0].Application.Should().Be("game, the best.exe");
        samples[0].FrameTimeMs.Should().BeApproximately(16.7, 1e-9);
    }

    [Fact]
    public void Parse_EscapedQuotes_AreUnescaped()
    {
        const string csv =
            "Application,msBetweenPresents\r\n" +
            "\"a\"\"b\",16.7\r\n";

        PresentMonCsvParser.Parse(csv)[0].Application.Should().Be("a\"b");
    }

    [Fact]
    public void Parse_WithBom_IsStripped()
    {
        var csv = "\uFEFFApplication,msBetweenPresents\r\ng,16.7\r\n";
        var samples = PresentMonCsvParser.Parse(csv);
        samples.Should().ContainSingle();
        samples[0].Application.Should().Be("g");
    }

    [Fact]
    public void Parse_OversizedLine_IsSkipped()
    {
        var huge = new string('x', 200_001) + ",16";
        var csv = "Application,msBetweenPresents\r\n" + huge + "\r\ng,16.7\r\n";

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().ContainSingle();
        samples[0].FrameTimeMs.Should().BeApproximately(16.7, 1e-9);
    }

    [Fact]
    public void Parse_MissingTimeColumn_BackfillsCumulativeSeconds()
    {
        const string csv =
            "Application,msBetweenPresents\r\n" +
            "g,16.0\r\n" +
            "g,16.0\r\n" +
            "g,16.0\r\n";

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().HaveCount(3);
        samples[0].TimeInSeconds.Should().BeApproximately(0, 1e-9);
        samples[1].TimeInSeconds.Should().BeApproximately(0.016, 1e-9);
        samples[2].TimeInSeconds.Should().BeApproximately(0.032, 1e-9);
    }

    [Fact]
    public void ParseFile_ReadsUtf8BomFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "framelab-csv-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var content = "Application,msBetweenPresents\r\ng,16.7\r\n";
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var samples = PresentMonCsvParser.ParseFile(path);

            samples.Should().ContainSingle();
            samples[0].Application.Should().Be("g");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ParseFile_MissingFile_ReturnsEmpty_NoThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), "framelab-missing-" + Guid.NewGuid().ToString("N") + ".csv");
        PresentMonCsvParser.ParseFile(path).Should().BeEmpty();
    }
}
