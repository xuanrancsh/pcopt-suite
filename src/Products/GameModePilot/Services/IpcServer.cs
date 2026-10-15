// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 命名管道 IPC 服务端（T18，架构 4.1 / ADR-5）。
/// 协议：命名管道 <c>\\.\pipe\pcopt.gamemodepilot</c>，JSON-line（每行一个请求，回写一行响应）。
/// 错误策略：单个连接异常不影响监听循环；命令执行异常返回 <c>{"ok":false,"error":...}</c> 而非断开。
/// 说明：本地管道、无网络暴露；产品 B/C 缺失或未启动时对端自行优雅降级。
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly IIpcCommandHandler _handler;
    private readonly ILogger<IpcServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    /// <summary>初始化 IPC 服务端。</summary>
    public IpcServer(IIpcCommandHandler handler, ILogger<IpcServer> logger)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>管道名。</summary>
    public string PipeName => IpcProtocol.DefaultPipeName;

    /// <summary>启动监听（后台任务，幂等）。</summary>
    public void Start()
    {
        if (_listenTask is { IsCompleted: false })
        {
            return;
        }

        _listenTask = Task.Run(() => ListenAsync(_cts.Token));
        _logger.LogInformation("IPC 服务端已启动（管道: {Pipe}）", IpcProtocol.PipePath);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已取消：忽略
        }

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期：取消即停止
            }
        }

        _cts.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleConnectionAsync(server, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                server?.Dispose();
                _logger.LogWarning("IPC 管道连接处理失败：{Message}", ex.Message);

                try
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await using (server)
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8);
                using var writer = new StreamWriter(server, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };

                string? line;
                while (!cancellationToken.IsCancellationRequested
                    && (line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                {
                    var response = await DispatchAsync(line).ConfigureAwait(false);
                    await writer.WriteLineAsync(IpcProtocol.Serialize(response)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                _logger.LogDebug(ex, "IPC 连接结束");
            }
        }
    }

    private async Task<IpcResponse> DispatchAsync(string line)
    {
        var parsed = IpcProtocol.ParseRequest(line);
        if (!parsed.IsSuccess)
        {
            return new IpcResponse(false, parsed.Error, null, null);
        }

        var request = parsed.Value!;
        try
        {
            switch (request.Cmd)
            {
                case IpcProtocol.CmdStatus:
                    return new IpcResponse(true, null, await _handler.GetStatusAsync().ConfigureAwait(false), null);

                case IpcProtocol.CmdEnable:
                    await _handler.EnableGuardianAsync(request.Minutes).ConfigureAwait(false);
                    return new IpcResponse(true, null, await _handler.GetStatusAsync().ConfigureAwait(false), null);

                case IpcProtocol.CmdDisable:
                    await _handler.DisableGuardianAsync().ConfigureAwait(false);
                    return new IpcResponse(true, null, await _handler.GetStatusAsync().ConfigureAwait(false), null);

                case IpcProtocol.CmdApplyProfile:
                    var apply = await _handler.ApplyProfileAsync(request.RuleIds).ConfigureAwait(false);
                    return apply.IsSuccess
                        ? new IpcResponse(true, null, await _handler.GetStatusAsync().ConfigureAwait(false), null)
                        : new IpcResponse(false, apply.Error, null, null);

                case IpcProtocol.CmdRevertAll:
                    var revert = await _handler.RevertAllAsync().ConfigureAwait(false);
                    return revert.IsSuccess
                        ? new IpcResponse(true, null, null, revert.Value)
                        : new IpcResponse(false, revert.Error, null, null);

                default:
                    return new IpcResponse(false, $"未知命令“{request.Cmd}”", null, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPC 命令执行异常");
            return new IpcResponse(false, "命令执行失败", null, null);
        }
    }
}
