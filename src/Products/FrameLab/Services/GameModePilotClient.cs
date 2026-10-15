// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PcOpt.Core;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 产品 A（GameModePilot）引擎调用抽象（T36，架构 4.1 IPC 契约）。
/// 抽象出来便于单测注入替身，验证"调 A 应用 / A 未运行则降级"两条路径。
/// </summary>
public interface IGameModePilotClient
{
    /// <summary>探测产品 A 是否常驻运行。</summary>
    bool IsRunning(int timeoutMs = 300);

    /// <summary>请求产品 A 应用指定规则集（<c>applyProfile</c>）。</summary>
    Result<string> ApplyProfile(IReadOnlyList<string> ruleIds);

    /// <summary>请求产品 A 全部还原（<c>revertAll</c>）。</summary>
    Result<string> RevertAll();
}

/// <summary>
/// 命名管道客户端（<c>\\.\pipe\pcopt.gamemodepilot</c>，JSON-line 协议）。
/// A 未运行时连接失败 → 返回明确中文错误，调用方据此降级到内置最小引擎。
/// </summary>
public sealed class GameModePilotClient : IGameModePilotClient
{
    /// <summary>命名管道名（不含前缀）。</summary>
    public const string PipeName = "pcopt.gamemodepilot";

    /// <summary>完整管道路径（文档 / UI 展示用）。</summary>
    public const string PipePath = @"\\.\pipe\pcopt.gamemodepilot";

    /// <inheritdoc/>
    public bool IsRunning(int timeoutMs = 300)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(timeoutMs);
            return client.IsConnected;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Result<string> ApplyProfile(IReadOnlyList<string> ruleIds)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        var payload = "{\"cmd\":\"applyProfile\",\"ruleIds\":["
            + string.Join(',', ruleIds.Select(id => "\"" + id.Replace("\"", string.Empty) + "\""))
            + "]}";
        return Send(payload);
    }

    /// <inheritdoc/>
    public Result<string> RevertAll() => Send("{\"cmd\":\"revertAll\"}");

    private static Result<string> Send(string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(1500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            writer.WriteLine(payload);
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return Result<string>.Fail("未收到「游戏模式」的响应");
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                return Result<string>.Ok("「游戏模式」已接受请求");
            }

            var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            return Result<string>.Fail(error ?? "「游戏模式」拒绝了本次请求");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Result<string>.Fail($"未能连接到「游戏模式」（{ex.Message}）");
        }
    }
}
