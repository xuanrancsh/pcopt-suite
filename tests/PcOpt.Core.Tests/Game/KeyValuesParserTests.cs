// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Game;

namespace PcOpt.Core.Tests.Game;

/// <summary><see cref="KeyValuesParser"/> 测试（T7：VDF/ACF 解析，纯函数）。</summary>
public sealed class KeyValuesParserTests
{
    [Fact]
    public void Parse_ClassicQuotedFormat_ShouldNestCorrectly()
    {
        var text = """
            "libraryfolders"
            {
                "0"
                {
                    "path"    "D:\\SteamLibrary"
                    "apps"
                    {
                        "730"    "123456"
                    }
                }
            }
            """;

        var root = KeyValuesParser.Parse(text);

        var library = KeyValuesParser.GetObject(root, "libraryfolders")!;
        var zero = KeyValuesParser.GetObject(library, "0")!;
        KeyValuesParser.GetString(zero, "path").Should().Be(@"D:\SteamLibrary");
        KeyValuesParser.GetString(KeyValuesParser.GetObject(zero, "apps")!, "730").Should().Be("123456");
    }

    [Fact]
    public void Parse_NewUnquotedKeyFormat_ShouldParse()
    {
        // Steam 2023+ 新风格：键不带引号
        var text = """
            "libraryfolders"
            {
            	"1"
            	{
            		"path"		"E:\\Games\\SteamLibrary"
            	}
            }
            """;

        var root = KeyValuesParser.Parse(text);

        var library = KeyValuesParser.GetObject(root, "libraryfolders")!;
        var one = KeyValuesParser.GetObject(library, "1")!;
        KeyValuesParser.GetString(one, "path").Should().Be(@"E:\Games\SteamLibrary");
    }

    [Fact]
    public void Parse_WithLineComments_ShouldSkipComments()
    {
        var text = """
            "root" // 顶层注释
            {
                // 内部注释
                "key" "value" // 行尾注释
            }
            """;

        var root = KeyValuesParser.Parse(text);

        KeyValuesParser.GetString(KeyValuesParser.GetObject(root, "root")!, "key").Should().Be("value");
    }

    [Fact]
    public void Parse_EscapedQuotesAndBackslashes_ShouldUnescape()
    {
        // 原始字符串字面量：反斜杠即字面反斜杠。VDF 转义规则：\\ → \，\" → "
        // 目标字面量 C:\Program Files\"Quoted"\x 的 VDF 编码为 C:\\Program Files\\\"Quoted\\\"\\x
        var text = """
            "root"
            {
                "path" "C:\\Program Files\\\"Quoted\\\"\\x"
            }
            """;

        var root = KeyValuesParser.Parse(text);

        KeyValuesParser.GetString(KeyValuesParser.GetObject(root, "root")!, "path")
            .Should().Be("C:\\Program Files\\\"Quoted\\\"\\x");
    }

    [Fact]
    public void Parse_AppManifestShape_ShouldReadAppState()
    {
        var text = """
            "AppState"
            {
                "appid"     "730"
                "name"      "Counter-Strike 2"
                "installdir" "Counter-Strike Global Offensive"
            }
            """;

        var root = KeyValuesParser.Parse(text);
        var appState = KeyValuesParser.GetObject(root, "AppState")!;

        KeyValuesParser.GetString(appState, "appid").Should().Be("730");
        KeyValuesParser.GetString(appState, "name").Should().Be("Counter-Strike 2");
        KeyValuesParser.GetString(appState, "installdir").Should().Be("Counter-Strike Global Offensive");
    }

    [Fact]
    public void Parse_UnclosedBrace_ShouldThrowFormatException()
    {
        var act = () => KeyValuesParser.Parse("\"root\"\n{\n\"key\" \"value\"\n");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_UnclosedQuote_ShouldThrowFormatException()
    {
        var act = () => KeyValuesParser.Parse("\"root\"\n{\n\"key\" \"unclosed\n}\n");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingValue_ShouldThrowFormatException()
    {
        var act = () => KeyValuesParser.Parse("\"root\"\n{\n\"key\"\n}\n");

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void TryParse_InvalidContent_ShouldReturnNull()
    {
        KeyValuesParser.TryParse("这不是 KeyValues").Should().BeNull();
        KeyValuesParser.TryParse(null).Should().BeNull();
        KeyValuesParser.TryParse("").Should().BeNull();
    }
}
