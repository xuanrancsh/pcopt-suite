// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tests.Native;

/// <summary>
/// <see cref="ConsoleOutputDecoder"/> 单元测试（T43）：OEM 代码页解析 + 编解码往返。
/// </summary>
/// <remarks>
/// 区域无关性：<see cref="Decode_RoundTrips_Cjk_Text_When_OemCodePage_Supports_It"/>
/// 曾在 196c79f 的 CI 上失败——GitHub Actions 的 windows-latest 是 en-US 区域，
/// OEM 代码页为 437（美式 OEM），根本无法表示中文，硬断言中文往返即假失败。
/// 因此中文往返改为条件断言；同时新增与进程区域完全无关的
/// <see cref="OsCodePageEncoding"/> 直构测试，把「GBK 字节 → 中文」这条真正的
/// 回归钉死在任意 Windows 上。CI 安全：不起任何子进程，纯编码断言。
/// </remarks>
public sealed class ConsoleOutputDecoderTests
{
    /// <summary>中日韩 Windows 的控制台 OEM 代码页（这些区域下子进程才可能输出中文）。</summary>
    private static readonly int[] CjkOemCodePages = { 936, 950, 932, 949, 1361 };

    /// <summary>输出编码必须取当前区域性的 OEM 代码页（中文 Windows 为 936/GBK）。</summary>
    [Fact]
    public void OutputEncoding_Uses_CurrentCulture_OemCodePage()
    {
        var expected = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;

        ConsoleOutputDecoder.OutputEncoding.CodePage.Should().Be(expected);
    }

    /// <summary>ASCII 文本在任意 OEM 代码页下均可表示，必须原样往返（GUID 等关键信息不能坏）。</summary>
    [Fact]
    public void Decode_RoundTrips_Ascii_Text_On_Any_Code_Page()
    {
        const string text = "powercfg /list 381b4222-f694-41f0-9685-ff5bb260df2e";

        var bytes = ConsoleOutputDecoder.OutputEncoding.GetBytes(text);

        ConsoleOutputDecoder.Decode(bytes).Should().Be(text);
    }

    /// <summary>
    /// 中文文本往返：仅当当前 OEM 代码页本身能表示中文时才断言。
    /// 非 CJK 代码页（如 en-US 的 437）下子进程根本不会输出中文，
    /// 硬断言会假失败——这正是 196c79f CI 红的原因。
    /// </summary>
    [Fact]
    public void Decode_RoundTrips_Cjk_Text_When_OemCodePage_Supports_It()
    {
        const string text = "平衡电源方案";

        var bytes = ConsoleOutputDecoder.OutputEncoding.GetBytes(text);

        if (CjkOemCodePages.Contains(ConsoleOutputDecoder.OutputEncoding.CodePage))
        {
            ConsoleOutputDecoder.Decode(bytes).Should().Be(text);
        }
        else
        {
            // 非 CJK 代码页：不可表示字符按代码页默认替换规则降级（'?'），但不得抛异常，
            // 且 ASCII 部分仍须完好——降级必须是优雅的。
            ConsoleOutputDecoder.Decode(bytes).Should().NotBeNull();
            ConsoleOutputDecoder.Decode(bytes).Should().NotContain(text);
        }
    }

    /// <summary>
    /// 直构 GBK 编码做中文往返，与进程区域完全无关（任意 Windows 上都执行）。
    /// 这是对「子进程按 OEM 代码页输出却被按 UTF-8 解码」这一原始缺陷的核心回归。
    /// </summary>
    [Fact]
    public void OsCodePageEncoding_Gbk_RoundTrips_Chinese_Text()
    {
        var gbk = new OsCodePageEncoding(936, "GBK");
        const string text = "平衡电源方案";

        var bytes = gbk.GetBytes(text);

        bytes.Should().BeEquivalentTo(new byte[]
        {
            0xC6, 0xBD, 0xBA, 0xE2, 0xB5, 0xE7,
            0xD4, 0xB4, 0xB7, 0xBD, 0xB0, 0xB8,
        });
        gbk.GetString(bytes).Should().Be(text);
    }

    /// <summary>
    /// 非 CJK 代码页（如 CI 的 en-US → 437）下中文不可表示：必须降级为 '?' 而不抛异常，
    /// ASCII 不受影响。钉住 CI 环境观测到的行为，避免将来「顺手修好」反而破坏预期。
    /// </summary>
    [Fact]
    public void OsCodePageEncoding_NonCjk_CodePage_Degrades_Gracefully()
    {
        var cp437 = new OsCodePageEncoding(437, "OEM 美式");

        cp437.GetBytes("平衡").Should().BeEquivalentTo(new byte[] { 0x3F, 0x3F });
        cp437.GetString(new byte[] { 0x3F, 0x3F }).Should().Be("??");
        cp437.GetBytes("powercfg").Should().BeEquivalentTo(Encoding.ASCII.GetBytes("powercfg"));
    }
}
