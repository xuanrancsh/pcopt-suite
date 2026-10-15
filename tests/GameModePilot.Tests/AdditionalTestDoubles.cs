// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>
/// 独立验收（T19）用可失败动作：<see cref="FailOnApply"/> 为 true 时 Apply 返回失败，
/// 用于构造"部分应用失败 / 还原失败"等攻击性场景。不触碰真实系统。
/// </summary>
/// <param name="Tag">标识。</param>
/// <param name="FailOnApply">Apply 是否失败。</param>
/// <param name="FailOnRevert">Revert 是否失败。</param>
public sealed record RudeAction(string Tag, bool FailOnApply = false, bool FailOnRevert = false) : TweakAction;

/// <summary>可失败动作处理器：记录调用，按动作配置返回成功 / 失败。</summary>
public sealed class RudeActionHandler : ITweakActionHandler
{
    /// <summary>调用记录（方法名 + 标识）。</summary>
    public List<(string Method, string Tag)> Calls { get; } = new();

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is RudeAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action) => Result<string>.Ok("{}");

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        var typed = Require(action);
        Calls.Add(("Apply", typed.Tag));
        return typed.FailOnApply ? Result.Fail($"模拟执行失败：{typed.Tag}") : Result.Ok();
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        var typed = Require(action);
        Calls.Add(("Revert", typed.Tag));
        return typed.FailOnRevert ? Result.Fail($"模拟还原失败：{typed.Tag}") : Result.Ok();
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action) => Result.Ok();

    private static RudeAction Require(TweakAction action) =>
        action as RudeAction ?? throw new ArgumentException("只接受 RudeAction", nameof(action));
}
