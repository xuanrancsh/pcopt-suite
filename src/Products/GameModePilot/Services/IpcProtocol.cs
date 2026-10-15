// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json;
using System.Text.Json.Serialization;
using PcOpt.Core;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// IPC 请求（JSON-line 协议，架构 4.1）。
/// </summary>
/// <param name="Cmd">命令：status / enable / disable / applyProfile / revertAll。</param>
/// <param name="Minutes">enable 的可选分钟数（守护保持时长）；其余命令忽略。</param>
/// <param name="RuleIds">applyProfile 的规则 Id 列表。</param>
public sealed record IpcRequest(string Cmd, int? Minutes, IReadOnlyList<string> RuleIds);

/// <summary>IPC 守护状态（status 命令返回）。</summary>
/// <param name="GuardianEnabled">守护是否启用。</param>
/// <param name="Phase">会话阶段（Guarding / Playing / Restored 等）。</param>
/// <param name="GameName">当前游戏名；无会话为 null。</param>
/// <param name="AppliedCount">已应用项数。</param>
/// <param name="AntiCheat">反作弊画像。</param>
public sealed record IpcStatus(
    bool GuardianEnabled,
    string Phase,
    string? GameName,
    int AppliedCount,
    string AntiCheat);

/// <summary>IPC 响应（JSON-line）。</summary>
/// <param name="Ok">命令是否成功。</param>
/// <param name="Error">失败原因；成功为 null。</param>
/// <param name="Status">status（及 enable/disable/applyProfile 成功后）的状态；否则 null。</param>
/// <param name="RevertedCount">revertAll 成功时还原的条目数；否则 null。</param>
public sealed record IpcResponse(bool Ok, string? Error, IpcStatus? Status, int? RevertedCount);

/// <summary>
/// 命名管道 IPC 协议（T18，ADR-5）：命名管道 <c>\\.\pipe\pcopt.gamemodepilot</c> + JSON-line。
/// 纯解析 / 序列化逻辑（无 IO），便于单元测试覆盖异常输入与命令校验。
/// </summary>
public static class IpcProtocol
{
    /// <summary>命名管道名（NamedPipeServerStream 使用，不含 <c>\\.\pipe\</c> 前缀）。</summary>
    public const string DefaultPipeName = "pcopt.gamemodepilot";

    /// <summary>完整管道路径（文档 / 调用方展示用）。</summary>
    public const string PipePath = @"\\.\pipe\pcopt.gamemodepilot";

    /// <summary>命令：查询守护状态。</summary>
    public const string CmdStatus = "status";

    /// <summary>命令：启用 / 保持守护（可带 minutes）。</summary>
    public const string CmdEnable = "enable";

    /// <summary>命令：暂停守护。</summary>
    public const string CmdDisable = "disable";

    /// <summary>命令：按规则 Id 应用（产品 C 调用）。</summary>
    public const string CmdApplyProfile = "applyProfile";

    /// <summary>命令：全部还原（产品 B/C 调用）。</summary>
    public const string CmdRevertAll = "revertAll";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>解析一行 JSON 请求；非法输入返回明确中文错误（不抛异常）。</summary>
    public static Result<IpcRequest> ParseRequest(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Result<IpcRequest>.Fail("IPC 请求为空");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            return Result<IpcRequest>.Fail($"IPC 请求不是合法 JSON：{ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<IpcRequest>.Fail("IPC 请求必须是 JSON 对象");
            }

            if (!root.TryGetProperty("cmd", out var cmdElement) || cmdElement.ValueKind != JsonValueKind.String)
            {
                return Result<IpcRequest>.Fail("IPC 请求缺少 cmd 字段（字符串）");
            }

            var cmd = cmdElement.GetString()?.Trim() ?? string.Empty;
            if (cmd.Length == 0)
            {
                return Result<IpcRequest>.Fail("IPC 请求的 cmd 不能为空");
            }

            int? minutes = null;
            if (root.TryGetProperty("minutes", out var minutesElement)
                && minutesElement.ValueKind == JsonValueKind.Number
                && minutesElement.TryGetInt32(out var parsedMinutes))
            {
                minutes = parsedMinutes;
            }

            var ruleIds = ReadRuleIds(root);

            return cmd switch
            {
                CmdStatus or CmdEnable or CmdDisable or CmdRevertAll => Result<IpcRequest>.Ok(new IpcRequest(cmd, minutes, ruleIds)),
                CmdApplyProfile when ruleIds.Count == 0 => Result<IpcRequest>.Fail("applyProfile 需要非空的 ruleIds 数组"),
                CmdApplyProfile => Result<IpcRequest>.Ok(new IpcRequest(cmd, minutes, ruleIds)),
                _ => Result<IpcRequest>.Fail($"未知命令“{cmd}”（支持：status/enable/disable/applyProfile/revertAll）"),
            };
        }
    }

    /// <summary>序列化为一行 JSON（camelCase，忽略 null）。</summary>
    public static string Serialize(IpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    private static IReadOnlyList<string> ReadRuleIds(JsonElement root)
    {
        if (!root.TryGetProperty("ruleIds", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var ruleIds = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                ruleIds.Add(value.Trim());
            }
        }

        return ruleIds;
    }
}
