// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json;
using FluentAssertions;
using PcOpt.Products.GameModePilot.Services;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// <see cref="IpcProtocol"/> 单元测试（T18）：JSON-line 命令解析与响应序列化。
/// </summary>
public sealed class IpcProtocolTests
{
    [Fact]
    public void ParseRequest_Status_ShouldSucceed()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"status"}""");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Cmd.Should().Be(IpcProtocol.CmdStatus);
    }

    [Fact]
    public void ParseRequest_EnableWithMinutes_ShouldCaptureMinutes()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"enable","minutes":60}""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Cmd.Should().Be(IpcProtocol.CmdEnable);
        result.Value.Minutes.Should().Be(60);
    }

    [Fact]
    public void ParseRequest_Disable_ShouldSucceed()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"disable"}""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Cmd.Should().Be(IpcProtocol.CmdDisable);
    }

    [Fact]
    public void ParseRequest_ApplyProfile_ShouldCaptureRuleIds()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"applyProfile","ruleIds":["game.timer.resolution","game.power.activate"]}""");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RuleIds.Should().Equal("game.timer.resolution", "game.power.activate");
    }

    [Fact]
    public void ParseRequest_ApplyProfileWithoutRuleIds_ShouldFail()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"applyProfile"}""");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("ruleIds");
    }

    [Fact]
    public void ParseRequest_RevertAll_ShouldSucceed()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"revertAll"}""");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Cmd.Should().Be(IpcProtocol.CmdRevertAll);
    }

    [Fact]
    public void ParseRequest_UnknownCommand_ShouldFail()
    {
        var result = IpcProtocol.ParseRequest("""{"cmd":"explode"}""");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("未知命令");
    }

    [Fact]
    public void ParseRequest_MissingCmd_ShouldFail()
    {
        var result = IpcProtocol.ParseRequest("""{"minutes":10}""");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("cmd");
    }

    [Fact]
    public void ParseRequest_MalformedJson_ShouldFail()
    {
        var result = IpcProtocol.ParseRequest("{not json");

        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseRequest_Blank_ShouldFail(string line)
    {
        IpcProtocol.ParseRequest(line).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Serialize_Response_ShouldBeCamelCaseJson()
    {
        var response = new IpcResponse(true, null, new IpcStatus(true, "Playing", "反恐精英 2", 4, "None"), null);

        var json = IpcProtocol.Serialize(response);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("status").GetProperty("phase").GetString().Should().Be("Playing");
        // 忽略 null 字段
        document.RootElement.TryGetProperty("error", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_ErrorResponse_ShouldContainError()
    {
        var json = IpcProtocol.Serialize(new IpcResponse(false, "命令执行失败", null, null));

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("error").GetString().Should().Be("命令执行失败");
    }
}
