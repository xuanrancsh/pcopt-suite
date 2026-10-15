// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19）IPC 契约健壮性：五类命令 + 畸形 / 超长 / 未知输入 + 序列化往返。
/// </summary>
public sealed class IpcProtocolRobustnessTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("revertAll")]
    public void ParseRequest_FourSimpleCommands_ShouldSucceed(string cmd)
    {
        IpcProtocol.ParseRequest($$"""{"cmd":"{{cmd}}"}""").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ParseRequest_NonObjectJson_ShouldFail()
    {
        IpcProtocol.ParseRequest("[1,2,3]").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseRequest_CmdNotString_ShouldFail()
    {
        IpcProtocol.ParseRequest("""{"cmd":123}""").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseRequest_EmptyCmdString_ShouldFail()
    {
        IpcProtocol.ParseRequest("""{"cmd":"   "}""").IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseRequest_UnknownCommand_ShouldFailWithHint()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"launchMissiles"}""");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未知命令");
    }

    [Fact]
    public void ParseRequest_ApplyProfile_WithNonStringRuleIds_ShouldRejectAsEmpty()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"applyProfile","ruleIds":[1,2]}""");

        result.IsSuccess.Should().BeFalse("ruleIds 中无有效字符串项 → 视为空 → applyProfile 应失败");
    }

    [Fact]
    public void ParseRequest_ApplyProfile_WithMixedRuleIds_ShouldKeepOnlyStrings()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"applyProfile","ruleIds":["a",5,"b"]}""");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RuleIds.Should().Equal("a", "b");
    }

    [Fact]
    public void ParseRequest_MinutesNotNumber_ShouldIgnoreMinutes()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"enable","minutes":"sixty"}""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Minutes.Should().BeNull();
    }

    [Fact]
    public void ParseRequest_MinutesOverflowInt32_ShouldIgnoreMinutes()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"enable","minutes":99999999999999}""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Minutes.Should().BeNull();
    }

    [Fact]
    public void ParseRequest_VeryLongInput_ShouldNotThrowAndShouldFail()
    {
        var line = "{" + new string('x', 500_000) + "}";

        var act = () => IpcProtocol.ParseRequest(line);

        act.Should().NotThrow();
        act().IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void ParseRequest_CommandWithSurroundingWhitespace_ShouldBeTrimmed()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"  status  "}""");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Cmd.Should().Be(IpcProtocol.CmdStatus);
    }

    [Fact]
    public void Serialize_ShouldOmitNullFields_AndUseCamelCase()
    {
        var json = IpcProtocol.Serialize(new IpcResponse(true, null, null, 3));

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"revertedCount\":3");
        json.Should().NotContain("error");
        json.Should().NotContain("status");
    }

    [Fact]
    public void ParseRequest_ExtraUnknownFields_ShouldBeIgnored()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"status","foo":42,"bar":{"x":1}}""");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Cmd.Should().Be(IpcProtocol.CmdStatus);
    }
}
