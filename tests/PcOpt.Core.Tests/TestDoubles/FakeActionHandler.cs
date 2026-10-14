// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>
/// 测试用动作处理器：记录全部调用，行为可通过委托注入。
/// </summary>
internal sealed class FakeActionHandler : ITweakActionHandler
{
    private readonly Type _handledType;

    public FakeActionHandler(Type handledType)
    {
        _handledType = handledType;
    }

    /// <summary>调用记录（动作实例 + 方法名）。</summary>
    public List<(TweakAction Action, string Method)> Calls { get; } = new();

    /// <summary>CaptureOriginal 行为注入；默认返回固定快照。</summary>
    public Func<TweakAction, Result<string>>? CaptureOriginalHandler { get; set; }

    /// <summary>Apply 行为注入；默认成功。</summary>
    public Func<TweakAction, Result>? ApplyHandler { get; set; }

    /// <summary>Revert 行为注入；默认成功。</summary>
    public Func<TweakAction, string, Result>? RevertHandler { get; set; }

    /// <summary>Verify 行为注入；默认成功。</summary>
    public Func<TweakAction, Result>? VerifyHandler { get; set; }

    public bool CanHandle(TweakAction action) => action.GetType() == _handledType;

    public Result<string> CaptureOriginal(TweakAction action)
    {
        Calls.Add((action, nameof(CaptureOriginal)));
        return CaptureOriginalHandler?.Invoke(action) ?? Result<string>.Ok("{\"fake\":true}");
    }

    public Result Apply(TweakAction action)
    {
        Calls.Add((action, nameof(Apply)));
        return ApplyHandler?.Invoke(action) ?? Result.Ok();
    }

    public Result Revert(TweakAction action, string originalValueJson)
    {
        Calls.Add((action, nameof(Revert)));
        return RevertHandler?.Invoke(action, originalValueJson) ?? Result.Ok();
    }

    public Result Verify(TweakAction action)
    {
        Calls.Add((action, nameof(Verify)));
        return VerifyHandler?.Invoke(action) ?? Result.Ok();
    }
}
