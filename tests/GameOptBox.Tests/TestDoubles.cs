// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using PcOpt.Core;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;
using PcOpt.UI.Controls;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>测试用动作（避免触碰真实系统）。</summary>
/// <param name="Tag">标识。</param>
public sealed record FakeAction(string Tag) : TweakAction;

/// <summary>记录型动作处理器：只记录调用，不触碰系统。</summary>
public sealed class FakeActionHandler : ITweakActionHandler
{
    /// <summary>调用记录。</summary>
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

/// <summary>固定系统环境替身（Win11 x64 / NVIDIA）。</summary>
public sealed class TestSystemEnvironment : ISystemEnvironment
{
    /// <inheritdoc/>
    public OsVersionInfo OsVersion { get; } = new("Windows", new Version(10, 0, 22631), 22631);

    /// <inheritdoc/>
    public CpuArchitecture CpuArchitecture { get; } = CpuArchitecture.X64;

    /// <inheritdoc/>
    public string GpuVendor { get; } = "NVIDIA";
}

/// <summary>还原点服务替身。</summary>
public sealed class FakeRestorePointService : IRestorePointService
{
    /// <summary>调用次数。</summary>
    public int Calls { get; private set; }

    /// <inheritdoc/>
    public Result<string> Create(string description)
    {
        Calls++;
        return Result<string>.Ok($"（替身）已创建还原点：{description}");
    }
}

/// <summary>记录型通知服务替身。</summary>
public sealed class RecordingNotifier : INotificationService
{
    /// <inheritdoc/>
    public event EventHandler<string>? ToastRequested;

    /// <summary>已推送的通知。</summary>
    public List<string> Messages { get; } = new();

    /// <inheritdoc/>
    public void Notify(string message)
    {
        Messages.Add(message);
        ToastRequested?.Invoke(this, message);
    }
}

/// <summary>可配置结果的确认对话框替身。</summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary>返回结果。</summary>
    public ConfirmDialogResult Result { get; set; } = new(true, false);

    /// <summary>调用次数。</summary>
    public int Calls { get; private set; }

    /// <summary>最近一次是否要求高危输入。</summary>
    public bool LastRequireTyped { get; private set; }

    /// <inheritdoc/>
    public ConfirmDialogResult Confirm(
        string title,
        string message,
        IReadOnlyList<string>? consequences = null,
        bool requireTypedConfirmation = false)
    {
        Calls++;
        LastRequireTyped = requireTypedConfirmation;
        return Result;
    }
}

/// <summary>临时目录（测试隔离，Dispose 时清理）。</summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>创建临时目录。</summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcopt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>目录路径。</summary>
    public string Path { get; }

    /// <summary>拼接子路径。</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // 忽略清理失败
        }
    }
}

/// <summary>仓库布局探测（定位 rules/tweaks 与 public.key）。</summary>
public static class RepoLayout
{
    /// <summary>向上查找含 pcopt-suite.sln 的仓库根目录。</summary>
    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "pcopt-suite.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("未能定位仓库根目录（pcopt-suite.sln）");
    }

    /// <summary>签名规则目录。</summary>
    public static string TweaksDirectory => System.IO.Path.Combine(FindRepoRoot(), "rules", "tweaks");

    /// <summary>签名公钥文件。</summary>
    public static string PublicKeyPath => System.IO.Path.Combine(FindRepoRoot(), "rules", "public.key");
}
