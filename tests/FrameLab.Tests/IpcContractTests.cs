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
        // 跨进程串行化：确保此刻没有其它测试进程在同一管道名上占位。
        using var ipcLock = AcquireIpcLock();

        var client = new GameModePilotClient();

        client.IsRunning(100).Should().BeFalse();
        var result = client.ApplyProfile(new[] { "power.highperf.activate" });
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private static (string Request, Result<string> Result) RoundTrip(string responseJson, Func<GameModePilotClient, Result<string>> call)
    {
        // 跨进程串行化：管道名是会话级全局资源，同一时刻只允许一个测试进程占用，
        // 否则多进程会实例耗尽、或客户端连到别的进程的服务端实例（串扰）。
        using var ipcLock = AcquireIpcLock();

        using var server = new NamedPipeServerStream(
            GameModePilotClient.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
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

        serverTask.Wait(30_000).Should().BeTrue("服务端应在宽裕窗口内完成一次请求-响应往返（慢速 CI 上线程池调度可能延迟，超时值须宽裕）");
        request.Should().NotBeNullOrWhiteSpace();
        return (request!, result);
    }

    /// <summary>
    /// 跨进程串行化锁名（会话内可见）。命名管道名 <c>pcopt.gamemodepilot</c> 是"会话级全局资源"：
    /// 多进程同时占位会导致（a）服务端实例耗尽、或（b）客户端连到别的进程的服务端实例（串扰，
    /// 使 <c>NoServer_*</c> / 各往返断言误判）。用命名互斥体确保同一时刻只有一个测试进程占用该管道名。
    /// 纯测试侧（产品无感；管道名字面量与契约校验语义完全不变）。
    /// </summary>
    private const string IpcSerializationMutexName = @"Local\pcopt-framelab-ipc-contract";

    private static IDisposable AcquireIpcLock()
    {
        var mutex = new Mutex(initiallyOwned: false, IpcSerializationMutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(90)))
            {
                mutex.Dispose();
                throw new TimeoutException("等待 IPC 契约测试跨进程锁超时（>90s）");
            }
        }
        catch (AbandonedMutexException)
        {
            // 上一个持有者异常退出：所有权已转移到本线程，继续即可。
        }

        return new MutexReleaser(mutex);
    }

    private sealed class MutexReleaser : IDisposable
    {
        private readonly Mutex _mutex;

        public MutexReleaser(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 当前线程并非持有者（理论不可达）：忽略。
            }

            _mutex.Dispose();
        }
    }
}
