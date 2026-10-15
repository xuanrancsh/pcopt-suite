// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// PresentMon CSV 解析「健壮性」独立攻击测试（QA 构造，T37 重点 3 / 架构 8.2）：
/// 在工程师既有用例之外，构造更多坏行 / 列别名 / 引号 / 超长行 / 空输入，确认<b>绝不抛异常</b>。
/// </summary>
public sealed class PresentMonCsvParserExtraTests
{
    [Theory]
    [InlineData("TimeInSeconds,MsFrameTime,CPUBusy,GpuBusy,MsUntilDisplayed")] // 大小写变体
    [InlineData("timeinseconds, msframetime , cpubusy , gpubusy , msuntildisplayed")] // 空格 + 小写
    [InlineData("timestamp,msframetime,cpu,gpu,msuntildisplayed")] // 简写别名
    public void Parse_ManyHeaderAliasVariants_AllMap(string header)
    {
        var csv = header + "\r\n1.000,16.700,2.5,15.0,20.1\r\n2.000,16.600,2.4,14.9,20.0\r\n";

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().HaveCount(2);
        samples[0].FrameTimeMs.Should().BeApproximately(16.700, 1e-9);
        samples[0].CpuMs.Should().BeApproximately(2.5, 1e-9);
        samples[0].GpuMs.Should().BeApproximately(15.0, 1e-9);
        samples[0].DisplayLatencyMs.Should().BeApproximately(20.1, 1e-9);
        samples[0].TimeInSeconds.Should().BeApproximately(1.000, 1e-9);
    }

    [Fact]
    public void Parse_QuotedHeaderNames_AreMatched()
    {
        const string csv = "\"Application\",\"msBetweenPresents\"\r\n\"game.exe\",\"16.7\"\r\n";
        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().ContainSingle();
        samples[0].Application.Should().Be("game.exe");
        samples[0].FrameTimeMs.Should().BeApproximately(16.7, 1e-9);
    }

    [Fact]
    public void Parse_ReorderedWithUnknownExtraColumns_StillMaps()
    {
        const string csv =
            "Junk,Application,AnotherJunk,msBetweenPresents,msUntilDisplayed\r\n" +
            "x,game.exe,y,16.7,20.1\r\n";

        var samples = PresentMonCsvParser.Parse(csv);

        samples.Should().ContainSingle();
        samples[0].Application.Should().Be("game.exe");
        samples[0].FrameTimeMs.Should().BeApproximately(16.7, 1e-9);
        samples[0].DisplayLatencyMs.Should().BeApproximately(20.1, 1e-9);
    }

    [Fact]
    public void Parse_RowWithFewerOrMoreColumns_IsSkipped()
    {
        const string csv =
            "Application,msBetweenPresents\r\n" +
            "g,16.7\r\n" +        // 有效
            "g\r\n" +             // 少列 → 跳过
            "g,16.6,99\r\n" +     // 多列 → 跳过
            "g,16.5\r\n";         // 有效

        var samples = PresentMonCsvParser.Parse(csv);
        samples.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_UnknownOnlyHeader_ReturnsEmpty()
    {
        PresentMonCsvParser.Parse("Foo,Bar,Baz\r\n1,2,3\r\n").Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullContent_ReturnsEmpty_NoThrow()
    {
        PresentMonCsvParser.Parse((string)null!).Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedBattery_NeverThrows()
    {
        var cases = new[]
        {
            "\r\n\r\n,\r\n,,,\r\n",                 // 全空列
            "\u0000\u0001\u0002\r\n",               // 控制字符
            "Application,msBetweenPresents\r\n\uD83D\uDE00,16.7\r\n", // emoji 应用名
            "Application,msBetweenPresents\r\n\"未闭合引号,16.7\r\n", // 未闭合引号
            "Application,msBetweenPresents\r\n\"" + new string('x', 5000) + "\",16.7\r\n", // 引号超长
            "Application,msBetweenPresents\r\n,16.7\r\n",           // 空应用名
            "Application,msBetweenPresents\r\n   , 16.7 \r\n",      // 空白填充
            new string('a', 200_001) + ",16\r\n",                   // 超长行
            ",\r\n,\r\n,\r\n",
            "Application,msBetweenPresents",                        // 无换行结尾
        };

        foreach (var csv in cases)
        {
            var act = () => PresentMonCsvParser.Parse(csv);
            act.Should().NotThrow($"输入不应导致异常：{Describe(csv)}");
        }
    }

    [Fact]
    public void Parse_VeryManyRows_AreAllCaptured()
    {
        var builder = new StringBuilder("Application,msBetweenPresents\r\n");
        for (var i = 0; i < 20_000; i++)
        {
            builder.Append("g,16.7\r\n");
        }

        PresentMonCsvParser.Parse(builder.ToString()).Should().HaveCount(20_000);
    }

    [Fact]
    public void ParseFile_PathIsDirectory_ReturnsEmpty_NoThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "framelab-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            PresentMonCsvParser.ParseFile(dir).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Describe(string s) =>
        s.Length > 24 ? s[..24].Replace("\r", "\\r").Replace("\n", "\\n") + "…" : s;
}
