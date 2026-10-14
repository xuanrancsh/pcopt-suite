// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// 服务启动类型处理器（架构 3.1 / T4）。
/// 快照记录原启动类型；Apply 经 SCM <c>ChangeServiceConfigW</c> 修改（幂等：已是目标值则跳过）；
/// Revert 按快照写回原启动类型；Verify 读回比对。
/// </summary>
public sealed class ServiceStartTypeActionHandler : ITweakActionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ServiceControl _serviceControl;
    private readonly ILogger<ServiceStartTypeActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public ServiceStartTypeActionHandler(ServiceControl serviceControl, ILogger<ServiceStartTypeActionHandler> logger)
    {
        _serviceControl = serviceControl ?? throw new ArgumentNullException(nameof(serviceControl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is ServiceStartTypeAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _serviceControl.GetStartType(typed.ServiceName);
        if (!current.IsSuccess)
        {
            return Result<string>.Fail(current.Error);
        }

        var snapshot = new ServiceStartTypeSnapshot { StartType = current.Value };
        return Result<string>.Ok(JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _serviceControl.GetStartType(typed.ServiceName);
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value == typed.Target)
        {
            // 幂等：已是目标启动类型
            return Result.Ok();
        }

        var result = _serviceControl.SetStartType(typed.ServiceName, typed.Target);
        if (result.IsSuccess)
        {
            _logger.LogDebug("服务启动类型已修改（服务: {ServiceName}）", typed.ServiceName);
        }

        return result;
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        var typed = RequireAction(action);
        ServiceStartTypeSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ServiceStartTypeSnapshot>(originalValueJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("还原日志中的服务快照损坏");
        }

        if (snapshot is null)
        {
            return Result.Fail("还原日志中的服务快照为空");
        }

        var current = _serviceControl.GetStartType(typed.ServiceName);
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        if (current.Value == snapshot.StartType)
        {
            // 已是原值（用户手动改回或其他还原已生效）
            return Result.Ok();
        }

        var result = _serviceControl.SetStartType(typed.ServiceName, snapshot.StartType);
        if (result.IsSuccess)
        {
            _logger.LogDebug("服务启动类型已还原（服务: {ServiceName}）", typed.ServiceName);
        }

        return result;
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        var typed = RequireAction(action);
        var current = _serviceControl.GetStartType(typed.ServiceName);
        if (!current.IsSuccess)
        {
            return Result.Fail(current.Error);
        }

        return current.Value == typed.Target
            ? Result.Ok()
            : Result.Fail($"校验失败：服务“{typed.ServiceName}”启动类型为 {current.Value}，期望 {typed.Target}");
    }

    private static ServiceStartTypeAction RequireAction(TweakAction action) =>
        action as ServiceStartTypeAction
        ?? throw new ArgumentException($"本处理器只接受 ServiceStartTypeAction，收到 {action.GetType().Name}", nameof(action));

    private sealed class ServiceStartTypeSnapshot
    {
        public ServiceStartMode StartType { get; set; }
    }
}
