// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Native;

namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// 电源计划处理器（架构 3.1 / T5）。
/// 语义（快照记录 <c>{ mode, targetGuid, originalActiveGuid }</c>）：
/// <list type="bullet">
/// <item>SetActive：激活目标 GUID；还原 = 切回原活动方案；</item>
/// <item>Import：导入 .pow；还原 = 先切回原活动方案，再删除导入的方案（Ultimate Performance 等内置 GUID 可删）；</item>
/// <item>Delete：删除方案；还原 = 明确失败（已删除的方案无法自动重建，需用户在系统电源选项处理）；</item>
/// <item>List：只读查询，无需还原。</item>
/// </list>
/// </summary>
public sealed class PowerCfgActionHandler : ITweakActionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PowerCfgController _controller;
    private readonly ILogger<PowerCfgActionHandler> _logger;

    /// <summary>初始化处理器。</summary>
    public PowerCfgActionHandler(PowerCfgController controller, ILogger<PowerCfgActionHandler> logger)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is PowerCfgAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        var typed = RequireAction(action);
        var active = _controller.GetActiveSchemeGuid();
        if (!active.IsSuccess)
        {
            return Result<string>.Fail(active.Error);
        }

        var snapshot = new PowerCfgSnapshot
        {
            Mode = typed.Kind,
            TargetGuid = typed.Kind == PowerActionKind.SetActive ? typed.Argument.Trim() : null,
            OriginalActiveGuid = active.Value.ToString("D", CultureInfo.InvariantCulture),
        };
        return Result<string>.Ok(JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = RequireAction(action);
        switch (typed.Kind)
        {
            case PowerActionKind.List:
            {
                var list = _controller.ListSchemes();
                return list.IsSuccess ? Result.Ok() : Result.Fail(list.Error);
            }

            case PowerActionKind.SetActive:
            {
                if (!Guid.TryParse(typed.Argument.Trim(), out var targetGuid))
                {
                    return Result.Fail($"电源方案 GUID 无效：{typed.Argument}");
                }

                var active = _controller.GetActiveSchemeGuid();
                if (!active.IsSuccess)
                {
                    return Result.Fail(active.Error);
                }

                if (active.Value == targetGuid)
                {
                    // 幂等：已是目标活动方案
                    return Result.Ok();
                }

                var result = _controller.SetActiveScheme(targetGuid);
                if (result.IsSuccess)
                {
                    _logger.LogDebug("电源方案已激活（目标 GUID: {SchemeGuid}）", targetGuid);
                }

                return result;
            }

            case PowerActionKind.Import:
            {
                var import = _controller.ImportScheme(typed.Argument);
                if (!import.IsSuccess)
                {
                    return Result.Fail(import.Error);
                }

                _logger.LogDebug("电源方案已导入（新 GUID: {SchemeGuid}）", import.Value);
                return Result.Ok();
            }

            case PowerActionKind.Delete:
            {
                if (!Guid.TryParse(typed.Argument.Trim(), out var targetGuid))
                {
                    return Result.Fail($"电源方案 GUID 无效：{typed.Argument}");
                }

                var result = _controller.DeleteScheme(targetGuid);
                if (result.IsSuccess)
                {
                    _logger.LogDebug("电源方案已删除（目标 GUID: {SchemeGuid}）", targetGuid);
                }

                return result;
            }

            default:
                return Result.Fail($"不支持的电源计划操作：{typed.Kind}");
        }
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        PowerCfgSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<PowerCfgSnapshot>(originalValueJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Fail("还原日志中的电源方案快照损坏");
        }

        if (snapshot is null || !Guid.TryParse(snapshot.OriginalActiveGuid, out var originalGuid))
        {
            return Result.Fail("还原日志中的电源方案快照无效");
        }

        switch (snapshot.Mode)
        {
            case PowerActionKind.List:
                return Result.Ok();

            case PowerActionKind.SetActive:
            {
                var active = _controller.GetActiveSchemeGuid();
                if (!active.IsSuccess)
                {
                    return Result.Fail(active.Error);
                }

                if (active.Value == originalGuid)
                {
                    return Result.Ok();
                }

                return _controller.SetActiveScheme(originalGuid);
            }

            case PowerActionKind.Import:
            {
                // 先切回原活动方案，再删除导入的方案（LIFO 的第二步由引擎保证在本动作之后执行删除的规则项）
                var active = _controller.GetActiveSchemeGuid();
                if (!active.IsSuccess)
                {
                    return Result.Fail(active.Error);
                }

                if (active.Value != originalGuid)
                {
                    var restore = _controller.SetActiveScheme(originalGuid);
                    if (!restore.IsSuccess)
                    {
                        return restore;
                    }
                }

                if (!string.IsNullOrWhiteSpace(snapshot.TargetGuid)
                    && Guid.TryParse(snapshot.TargetGuid, out var importedGuid)
                    && importedGuid != originalGuid)
                {
                    return _controller.DeleteScheme(importedGuid);
                }

                return Result.Ok();
            }

            case PowerActionKind.Delete:
                return Result.Fail("已删除的电源方案无法自动还原（请到系统电源选项重新创建）");

            default:
                return Result.Fail($"不支持的电源计划操作：{snapshot.Mode}");
        }
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        var typed = RequireAction(action);
        switch (typed.Kind)
        {
            case PowerActionKind.List:
                return _controller.ListSchemes().IsSuccess
                    ? Result.Ok()
                    : Result.Fail("校验失败：无法列出电源方案");

            case PowerActionKind.SetActive:
            {
                if (!Guid.TryParse(typed.Argument.Trim(), out var targetGuid))
                {
                    return Result.Fail($"电源方案 GUID 无效：{typed.Argument}");
                }

                var active = _controller.GetActiveSchemeGuid();
                if (!active.IsSuccess)
                {
                    return Result.Fail(active.Error);
                }

                return active.Value == targetGuid
                    ? Result.Ok()
                    : Result.Fail("校验失败：当前电源方案不是目标方案");
            }

            case PowerActionKind.Import:
                // 导入操作的正确性由 powercfg 退出码保证；此处仅确认方案列表可读
                return _controller.ListSchemes().IsSuccess
                    ? Result.Ok()
                    : Result.Fail("校验失败：无法列出电源方案");

            case PowerActionKind.Delete:
            {
                if (!Guid.TryParse(typed.Argument.Trim(), out var targetGuid))
                {
                    return Result.Fail($"电源方案 GUID 无效：{typed.Argument}");
                }

                var schemes = _controller.ListSchemes();
                if (!schemes.IsSuccess)
                {
                    return Result.Fail(schemes.Error);
                }

                return schemes.Value!.Any(s => s.Guid == targetGuid)
                    ? Result.Fail("校验失败：目标电源方案仍存在")
                    : Result.Ok();
            }

            default:
                return Result.Fail($"不支持的电源计划操作：{typed.Kind}");
        }
    }

    private static PowerCfgAction RequireAction(TweakAction action) =>
        action as PowerCfgAction
        ?? throw new ArgumentException($"本处理器只接受 PowerCfgAction，收到 {action.GetType().Name}", nameof(action));

    private sealed class PowerCfgSnapshot
    {
        public PowerActionKind Mode { get; set; }

        public string? TargetGuid { get; set; }

        public string OriginalActiveGuid { get; set; } = string.Empty;
    }
}
