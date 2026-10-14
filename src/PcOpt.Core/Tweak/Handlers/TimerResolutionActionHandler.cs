// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// 计时器分辨率处理器（ntdll <c>NtSetTimerResolution</c>，架构 3.1 / T5）。
/// 快照记录当前分辨率；Apply 请求目标值（幂等：已是目标则跳过）；
/// Revert 恢复原值（原值为 0 时释放请求）；Verify 读回比对。
/// 计时器请求为进程级：进程崩溃 / 强杀时由系统自动回收，正常退出由本处理器 Revert 恢复。
/// </summary>
public sealed class TimerResolutionActionHandler : ITweakActionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TimerResolutionController _controller;
    private readonly ILogger<TimerResolutionActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public TimerResolutionActionHandler(TimerResolutionController controller, ILogger<TimerResolutionActionHandler> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is TimerResolutionAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        _ = RequireAction(action);
        var current = _controller.QueryResolution();
        if (!current.IsSuccess)
        {
            return Result<string>.Fail(current.Error);
        }

        var snapshot = new TimerResolutionSnapshot { OriginalResolution100Ns = current.Value.Current100Ns };
        return Result<string>.Ok(JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _controller.QueryResolution();
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value.Current100Ns == typed.DesiredResolution100Ns)
        {
            // 幂等：已是目标分辨率
            return Result.Ok();
        }

        var setResult = _controller.Set(typed.DesiredResolution100Ns);
        if (setResult.IsSuccess)
        {
            _logger.LogDebug(
                "计时器分辨率已请求（目标: {Resolution100Ns}，100ns 单位）",
                typed.DesiredResolution100Ns.ToString(CultureInfo.InvariantCulture));
        }

        return setResult.IsSuccess ? Result.Ok() : Result.Fail(setResult.Error);
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        _ = RequireAction(action);
        TimerResolutionSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TimerResolutionSnapshot>(originalValueJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("还原日志中的计时器分辨率快照损坏");
        }

        if (snapshot is null)
        {
            return Result.Fail("还原日志中的计时器分辨率快照为空");
        }

        var current = _controller.QueryResolution();
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value.Current100Ns == snapshot.OriginalResolution100Ns)
        {
            return Result.Ok();
        }

        if (snapshot.OriginalResolution100Ns == 0)
        {
            return _controller.Release();
        }

        var restoreResult = _controller.Set(snapshot.OriginalResolution100Ns);
        return restoreResult.IsSuccess ? Result.Ok() : Result.Fail(restoreResult.Error);
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _controller.QueryResolution();
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        return current.Value.Current100Ns == typed.DesiredResolution100Ns
            ? Result.Ok()
            : Result.Fail(
                $"校验失败：当前计时器分辨率为 {current.Value.Current100Ns.ToString(CultureInfo.InvariantCulture)}，期望 {typed.DesiredResolution100Ns.ToString(CultureInfo.InvariantCulture)}（100ns 单位）");
    }

    private static TimerResolutionAction RequireAction(TweakAction action) =>
        action as TimerResolutionAction
        ?? throw new ArgumentException($"本处理器只接受 TimerResolutionAction，收到 {action.GetType().Name}", nameof(action));

    private sealed class TimerResolutionSnapshot
    {
        public uint OriginalResolution100Ns { get; set; }
    }
}
