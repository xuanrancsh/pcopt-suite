// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Native;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;

namespace PcOpt.Core.Tests.Handlers;

/// <summary>
/// <see cref="ScheduledTaskActionHandler"/> 单元测试（架构 3.1 / T4）。
/// 只覆盖"任务不存在"路径（无副作用、无需管理员）；真实任务启用 / 禁用属集成场景，留待 T23 真机验证。
/// </summary>
public sealed class ScheduledTaskActionHandlerTests
{
    private const string NonExistentTaskPath = @"\pcopt-test-nonexistent\no-such-task";

    private readonly ScheduledTaskController _controller = new();
    private readonly ScheduledTaskActionHandler _handler;

    public ScheduledTaskActionHandlerTests()
    {
        _handler = new ScheduledTaskActionHandler(_controller, NullLogger<ScheduledTaskActionHandler>.Instance);
    }

    [Fact]
    public void CaptureOriginal_NonExistentTask_ShouldRecordNotExists()
    {
        var action = new ScheduledTaskAction(NonExistentTaskPath, Enable: true);

        var snapshot = _handler.CaptureOriginal(action);

        snapshot.IsSuccess.Should().BeTrue(snapshot.Error);
        snapshot.Value.Should().Contain("\"TaskExists\":false");
    }

    [Fact]
    public void Apply_NonExistentTask_ShouldFailWithClearError()
    {
        var action = new ScheduledTaskAction(NonExistentTaskPath, Enable: true);

        var result = _handler.Apply(action);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("不存在");
    }

    [Fact]
    public void Revert_OriginallyMissingTask_ShouldSucceedAsNoOp()
    {
        var action = new ScheduledTaskAction(NonExistentTaskPath, Enable: true);
        var snapshot = _handler.CaptureOriginal(action).Value!;

        var revert = _handler.Revert(action, snapshot);

        revert.IsSuccess.Should().BeTrue(revert.Error);
    }

    [Fact]
    public void Verify_NonExistentTask_ShouldFail()
    {
        var action = new ScheduledTaskAction(NonExistentTaskPath, Enable: true);

        var verify = _handler.Verify(action);

        verify.IsSuccess.Should().BeFalse();
        verify.Error.Should().Contain("不存在");
    }

    [Fact]
    public void CanHandle_ShouldOnlyAcceptScheduledTaskAction()
    {
        _handler.CanHandle(new ScheduledTaskAction(@"\x\y", true)).Should().BeTrue();
        _handler.CanHandle(new ShellAction("cmd.exe", "")).Should().BeFalse();
    }

    [Theory]
    [InlineData(@"\pcopt-test\sample", @"\pcopt-test", "sample")]
    [InlineData(@"\sample", @"\", "sample")]
    [InlineData(@"\Microsoft\Windows\Maintenance\BrIdleTask", @"\Microsoft\Windows\Maintenance", "BrIdleTask")]
    public void SplitTaskPath_ShouldSplitFolderAndTaskName(string taskPath, string expectedFolder, string expectedName)
    {
        var (folder, name) = ScheduledTaskController.SplitTaskPath(taskPath);

        folder.Should().Be(expectedFolder);
        name.Should().Be(expectedName);
    }
}
