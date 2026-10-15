// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Win32;
using PcOpt.Core;
using PcOpt.Core.Game;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameModePilot.Tests;

/// <summary>测试用动作（避免触碰真实系统）。</summary>
/// <param name="Tag">标识，用于断言执行顺序。</param>
public sealed record FakeAction(string Tag) : TweakAction;

/// <summary>测试用动作处理器：记录调用，不触碰系统。</summary>
public sealed class FakeActionHandler : ITweakActionHandler
{
    /// <summary>调用记录（方法名 + 动作）。</summary>
    public List<(string Method, TweakAction Action)> Calls { get; } = new();

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is FakeAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        Calls.Add(("CaptureOriginal", action));
        return Result<string>.Ok("{}");
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        Calls.Add(("Apply", action));
        return Result.Ok();
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson)
    {
        Calls.Add(("Revert", action));
        return Result.Ok();
    }

    /// <inheritdoc/>
    public Result Verify(TweakAction action)
    {
        Calls.Add(("Verify", action));
        return Result.Ok();
    }
}

/// <summary>记录型注册表动作处理器：捕获 IFEO 降级产生的 RegistrySetAction，不触碰真实注册表。</summary>
public sealed class RecordingRegistryHandler : ITweakActionHandler
{
    /// <summary>处理过的动作（按调用顺序）。</summary>
    public List<RegistrySetAction> Actions { get; } = new();

    /// <summary>最近一次动作。</summary>
    public RegistrySetAction? LastAction => Actions.Count > 0 ? Actions[^1] : null;

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is RegistrySetAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        if (action is RegistrySetAction registry)
        {
            Actions.Add(registry);
        }

        return Result<string>.Ok("{}");
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action) => Result.Ok();

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson) => Result.Ok();

    /// <inheritdoc/>
    public Result Verify(TweakAction action) => Result.Ok();
}

/// <summary>记录型进程调度动作处理器：捕获 ProcessPriorityAction，不触碰真实进程。</summary>
public sealed class RecordingProcessHandler : ITweakActionHandler
{
    /// <summary>处理过的动作（按调用顺序）。</summary>
    public List<ProcessPriorityAction> Actions { get; } = new();

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => action is ProcessPriorityAction;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action)
    {
        if (action is ProcessPriorityAction process)
        {
            Actions.Add(process);
        }

        return Result<string>.Ok("{}");
    }

    /// <inheritdoc/>
    public Result Apply(TweakAction action) => Result.Ok();

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson) => Result.Ok();

    /// <inheritdoc/>
    public Result Verify(TweakAction action) => Result.Ok();
}

/// <summary>可注入的反作弊检测器替身。</summary>
public sealed class FakeAntiCheatDetector : IAntiCheatDetector
{
    private readonly AntiCheatDetectionResult _result;

    /// <summary>初始化替身。</summary>
    public FakeAntiCheatDetector(AntiCheatDetectionResult? result = null)
    {
        _result = result ?? AntiCheatDetectionResult.None;
    }

    /// <inheritdoc/>
    public AntiCheatDetectionResult Detect() => _result;
}

/// <summary>固定系统环境替身（Win11 x64，用于条件匹配）。</summary>
public sealed class TestSystemEnvironment : ISystemEnvironment
{
    /// <inheritdoc/>
    public OsVersionInfo OsVersion { get; } = new("Windows", new Version(10, 0, 22631), 22631);

    /// <inheritdoc/>
    public CpuArchitecture CpuArchitecture { get; } = CpuArchitecture.X64;

    /// <inheritdoc/>
    public string GpuVendor { get; } = "NVIDIA";
}
