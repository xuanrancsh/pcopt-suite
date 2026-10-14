// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;

namespace PcOpt.Core.Tests.Handlers;

/// <summary>
/// <see cref="ShellActionHandler"/> 单元测试（架构 3.1 / T5）。
/// </summary>
public sealed class ShellActionHandlerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ShellActionHandler _handler;

    public ShellActionHandlerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "pcopt-shell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _handler = new ShellActionHandler(NullLogger<ShellActionHandler>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Apply_EchoCommand_ShouldSucceed()
    {
        var action = new ShellAction("cmd.exe", "/c echo pcopt-test");

        var result = _handler.Apply(action);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public void Apply_MissingCommand_ShouldFailWithClearError()
    {
        var action = new ShellAction("pcopt-nonexistent-command-xyz", string.Empty);

        var result = _handler.Apply(action);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("无法执行命令");
    }

    [Fact]
    public void Apply_NonZeroExitCode_ShouldFail()
    {
        var action = new ShellAction("cmd.exe", "/c exit 3");

        var result = _handler.Apply(action);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("退出码");
    }

    [Fact]
    public void Apply_BatFile_ShouldRunViaCmd()
    {
        var markerPath = Path.Combine(_tempDirectory, "marker.txt");
        var batPath = Path.Combine(_tempDirectory, "probe.bat");
        File.WriteAllText(batPath, $"@echo off\r\necho ran > \"{markerPath}\"\r\n");

        var result = _handler.Apply(new ShellAction(batPath, string.Empty));

        result.IsSuccess.Should().BeTrue(result.Error);
        File.Exists(markerPath).Should().BeTrue();
    }

    [Fact]
    public void ResolveInvocation_BatFile_ShouldWrapWithCmd()
    {
        var (fileName, arguments) = ShellActionHandler.ResolveInvocation(@"C:\tools\run.bat", "--silent");

        fileName.Should().Be("cmd.exe");
        arguments.Should().Be("/c \"C:\\tools\\run.bat\" --silent");
    }

    [Fact]
    public void ResolveInvocation_PlainExe_ShouldNotWrap()
    {
        var (fileName, arguments) = ShellActionHandler.ResolveInvocation("powercfg", "/list");

        fileName.Should().Be("powercfg");
        arguments.Should().Be("/list");
    }

    [Fact]
    public void Revert_ShouldAlwaysFailWithClearMessage()
    {
        var action = new ShellAction("cmd.exe", "/c echo hi");

        var revert = _handler.Revert(action, "{}");

        revert.IsSuccess.Should().BeFalse();
        revert.Error.Should().Contain("无法自动还原");
    }

    [Fact]
    public void Verify_ShouldSucceed()
    {
        var verify = _handler.Verify(new ShellAction("cmd.exe", "/c echo hi"));

        verify.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CanHandle_ShouldOnlyAcceptShellAction()
    {
        _handler.CanHandle(new ShellAction("cmd.exe", "")).Should().BeTrue();
        _handler.CanHandle(new TimerResolutionAction(5000)).Should().BeFalse();
    }
}
