// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// 计划任务启用 / 禁用处理器（架构 3.1 / T4）。
/// 零 NuGet 依赖：经 <see cref="ScheduledTaskController"/> 的 COM 互操作实现。
/// 任务不存在时 Apply 返回明确失败（不创建、不猜测）。
/// </summary>
public sealed class ScheduledTaskActionHandler : ITweakActionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ScheduledTaskController _controller;
    private readonly ILogger<ScheduledTaskActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public ScheduledTaskActionHandler(ScheduledTaskController controller, ILogger<ScheduledTaskActionHandler> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is ScheduledTaskAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _controller.QueryEnabled(typed.TaskPath);
        if (!current.IsSuccess)
        {
            return Result<string>.Fail(current.Error);
        }

        var snapshot = new ScheduledTaskSnapshot
        {
            TaskExists = current.Value.HasValue,
            Enabled = current.Value ?? false,
        };
        return Result<string>.Ok(JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _controller.QueryEnabled(typed.TaskPath);
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value is null)
        {
            return Result.Fail($"计划任务不存在：{typed.TaskPath}");
        }

        if (current.Value == typed.Enable)
        {
            // 幂等：已是目标状态
            return Result.Ok();
        }

        var result = _controller.SetEnabled(typed.TaskPath, typed.Enable);
        if (result.IsSuccess)
        {
            _logger.LogDebug("计划任务状态已修改（任务路径: {TaskPath}）", typed.TaskPath);
        }

        return result;
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        var typed = RequireAction(action);
        ScheduledTaskSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ScheduledTaskSnapshot>(originalValueJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("还原日志中的计划任务快照损坏");
        }

        if (snapshot is null)
        {
            return Result.Fail("还原日志中的计划任务快照为空");
        }

        if (!snapshot.TaskExists)
        {
            // 原本就不存在，无可还原
            return Result.Ok();
        }

        var current = _controller.QueryEnabled(typed.TaskPath);
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value is null)
        {
            return Result.Fail($"计划任务已不存在，无法还原：{typed.TaskPath}");
        }

        if (current.Value == snapshot.Enabled)
        {
            return Result.Ok();
        }

        var result = _controller.SetEnabled(typed.TaskPath, snapshot.Enabled);
        if (result.IsSuccess)
        {
            _logger.LogDebug("计划任务状态已还原（任务路径: {TaskPath}）", typed.TaskPath);
        }

        return result;
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _controller.QueryEnabled(typed.TaskPath);
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value is null)
        {
            return Result.Fail($"校验失败：计划任务不存在：{typed.TaskPath}");
        }

        return current.Value == typed.Enable
            ? Result.Ok()
            : Result.Fail($"校验失败：计划任务 {typed.TaskPath} 启用状态为 {current.Value}，期望 {typed.Enable}");
    }

    private static ScheduledTaskAction RequireAction(TweakAction action) =>
        action as ScheduledTaskAction
        ?? throw new ArgumentException($"本处理器只接受 ScheduledTaskAction，收到 {action.GetType().Name}", nameof(action));

    private sealed class ScheduledTaskSnapshot
    {
        public bool TaskExists { get; set; }

        public bool Enabled { get; set; }
    }
}
