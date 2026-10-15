// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// IPC 命令处理器（T18）：由守护服务实现，IPC 服务端只负责协议解析与分发。
/// </summary>
public interface IIpcCommandHandler
{
    /// <summary>查询守护状态。</summary>
    Task<IpcStatus> GetStatusAsync();

    /// <summary>启用 / 保持守护（<paramref name="minutes"/> 为保持时长，null 表示直到显式暂停）。</summary>
    Task EnableGuardianAsync(int? minutes);

    /// <summary>暂停守护。</summary>
    Task DisableGuardianAsync();

    /// <summary>按规则 Id 应用（产品 C 调用）。</summary>
    Task<Result> ApplyProfileAsync(IReadOnlyList<string> ruleIds);

    /// <summary>全部还原；成功返回还原条目数。</summary>
    Task<Result<int>> RevertAllAsync();
}
