// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PcOpt.Core;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// 与产品 A 的 IPC 契约「字面量一致性」独立验证（QA 构造，T37 重点 7 / 架构 4.1）。
/// 起一个与 A 同名命名管道，抓取产品 C 真实客户端发出的 JSON-line，
/// 核对命令字面量与 A 的 <c>IpcProtocol</c>（cmd=applyProfile / revertAll、字段 ruleIds、响应 ok/error）一致。
/// </summary>
public sealed class IpcContractTests
{
    [Fact]
    public void PipeIdentity_MatchesGameModePilotContract()
    {
        GameModePilotClient.PipeName.Should().Be("pcopt.gamemodepilot");
        GameModePilotClient.PipePath.Should().Be(@"\\.\pipe\pcopt.gamemodepilot");
    }

    [Fact]
    public void ApplyProfile_SendsApplyProfileCommand_AndParsesOk()
    {
        var (request, result) = RoundTrip("{\"ok\":true}", client =>
            client.ApplyProfile(new[] { "latency.interrupt.affinity", "power.highperf.activate" }));

        result.IsSuccess.Should().BeTrue(result.Error);

        using var doc = JsonDocument.Parse(request);
        doc.RootElement.GetProperty("cmd").GetString().Should().Be("applyProfile");
        var ids = doc.RootElement.GetProperty("ruleIds").EnumerateArray().Select(e => e.GetString()).ToArray();
        ids.Should().Contain("latency.interrupt.affinity").And.Contain("power.highperf.activate");
    }

    [Fact]
    public void RevertAll_SendsRevertAllCommand_AndParsesOk()
    {
        var (request, result) = RoundTrip("{\"ok\":true,\"revertedCount\":3}", client => client.RevertAll());

        result.IsSuccess.Should().BeTrue(result.Error);
        using var doc = JsonDocument.Parse(request);
        doc.RootElement.GetProperty("cmd").GetString().Should().Be("revertAll");
    }

    [Fact]
    public void ServerErrorResponse_SurfacesHumanReadableError()
    {
        var (_, result) = RoundTrip("{\"ok\":false,\"error\":\"引擎拒绝了本次请求\"}", client =>
            client.ApplyProfile(new[] { "gpu.hags.enable" }));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("引擎拒绝了本次请求");
    }

    [Fact]
    public void NoServer_IsRunningFalse_AndApplyFailsGracefully()
    {
        var client = new GameModePilotClient();

        client.IsRunning(100).Should().BeFalse();
        var result = client.ApplyProfile(new[] { "power.highperf.activate" });
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private static (string Request, Result<string> Result) RoundTrip(string responseJson, Func<GameModePilotClient, Result<string>> call)
    {
        using var server = new NamedPipeServerStream(
            GameModePilotClient.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        string? request = null;
        var serverTask = Task.Run(() =>
        {
            server.WaitForConnection();
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            request = reader.ReadLine();
            writer.WriteLine(responseJson);
        });

        var result = call(new GameModePilotClient());

        serverTask.Wait(5_000).Should().BeTrue("服务端应完成一次请求-响应往返");
        request.Should().NotBeNullOrWhiteSpace();
        return (request!, result);
    }
}
