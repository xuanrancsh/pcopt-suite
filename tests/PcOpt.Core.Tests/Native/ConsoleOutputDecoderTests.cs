// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tests.Native;

/// <summary>
/// <see cref="ConsoleOutputDecoder"/> 单元测试（T43）：OEM 代码页解析 + 中文编解码往返。
/// CI 安全：不起任何子进程，纯编码断言。
/// </summary>
public sealed class ConsoleOutputDecoderTests
{
    /// <summary>输出编码必须取当前区域性的 OEM 代码页（中文 Windows 为 936/GBK）。</summary>
    [Fact]
    public void OutputEncoding_Uses_CurrentCulture_OemCodePage()
    {
        var expected = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;

        ConsoleOutputDecoder.OutputEncoding.CodePage.Should().Be(expected);
    }

    /// <summary>中文文本经输出编码取字节再 Decode 必须原样往返（修复 GBK 乱码回归）。</summary>
    [Fact]
    public void Decode_RoundTrips_Chinese_Text()
    {
        const string text = "平衡电源方案";

        var bytes = ConsoleOutputDecoder.OutputEncoding.GetBytes(text);

        ConsoleOutputDecoder.Decode(bytes).Should().Be(text);
    }
}
