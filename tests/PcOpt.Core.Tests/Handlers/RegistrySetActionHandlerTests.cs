// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;

namespace PcOpt.Core.Tests.Handlers;

/// <summary>
/// <see cref="RegistrySetActionHandler"/> 单元测试（架构 T4 验收：apply → revert 一致性 100%）。
/// 全部使用 HKCU\Software\pcopt-test-{guid} 临时键，严禁触碰系统键。
/// </summary>
public sealed class RegistrySetActionHandlerTests : IDisposable
{
    private const string TestKeyRelativePathPrefix = @"Software\pcopt-test-";

    private readonly string _testKeyRelativePath;
    private readonly RegistrySetActionHandler _handler;

    public RegistrySetActionHandlerTests()
    {
        _testKeyRelativePath = TestKeyRelativePathPrefix + Guid.NewGuid().ToString("N");
        _handler = new RegistrySetActionHandler(NullLogger<RegistrySetActionHandler>.Instance);
    }

    public void Dispose()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        baseKey.DeleteSubKeyTree(_testKeyRelativePath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void ApplyThenRevert_NewDWord_ShouldRestoreAbsentState()
    {
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "TestValue", 1, RegistryValueKind.DWord);

        var snapshot = _handler.CaptureOriginal(action);
        snapshot.IsSuccess.Should().BeTrue(snapshot.Error);
        snapshot.Value.Should().Contain("\"ValueExists\":false");

        var apply = _handler.Apply(action);
        apply.IsSuccess.Should().BeTrue(apply.Error);
        ReadRawValue("TestValue").Should().Be(1);

        _handler.Verify(action).IsSuccess.Should().BeTrue();

        var revert = _handler.Revert(action, snapshot.Value!);
        revert.IsSuccess.Should().BeTrue(revert.Error);

        // 原键不存在 → 还原后整个子键消失（不残留空键）
        ReadRawValue("TestValue").Should().BeNull();
        TestKeyExists().Should().BeFalse();
    }

    [Fact]
    public void ApplyThenRevert_ExistingString_ShouldRestoreExactValue()
    {
        CreateTestKey();
        WriteRawValue("Text", "原始值", RegistryValueKind.String);
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "Text", "新值", RegistryValueKind.String);

        var snapshot = _handler.CaptureOriginal(action);
        snapshot.Value.Should().Contain("原始值");

        _handler.Apply(action).IsSuccess.Should().BeTrue();
        ReadRawValue("Text").Should().Be("新值");

        var revert = _handler.Revert(action, snapshot.Value!);
        revert.IsSuccess.Should().BeTrue(revert.Error);
        ReadRawValue("Text").Should().Be("原始值");
    }

    [Fact]
    public void Revert_OriginallyMissingValue_ShouldDeleteValueButKeepKey()
    {
        CreateTestKey();
        WriteRawValue("Keep", "保留", RegistryValueKind.String);
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "NewValue", 42, RegistryValueKind.DWord);

        var snapshot = _handler.CaptureOriginal(action);
        snapshot.Value.Should().Contain("\"ValueExists\":false");
        snapshot.Value.Should().Contain("\"KeyExists\":true");

        _handler.Apply(action).IsSuccess.Should().BeTrue();
        ReadRawValue("NewValue").Should().Be(42);

        var revert = _handler.Revert(action, snapshot.Value!);
        revert.IsSuccess.Should().BeTrue(revert.Error);

        // 值被删除，键保留（含其他值）
        ReadRawValue("NewValue").Should().BeNull();
        ReadRawValue("Keep").Should().Be("保留");
        TestKeyExists().Should().BeTrue();
    }

    [Fact]
    public void Apply_ShouldBeIdempotent()
    {
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "Idem", 7, RegistryValueKind.DWord);

        _handler.Apply(action).IsSuccess.Should().BeTrue();
        _handler.Apply(action).IsSuccess.Should().BeTrue();
        _handler.Apply(action).IsSuccess.Should().BeTrue();

        ReadRawValue("Idem").Should().Be(7);
    }

    [Fact]
    public void QWordRoundTrip_ShouldPreserveValue()
    {
        CreateTestKey();
        WriteRawValue("Q", 9007199254740993L, RegistryValueKind.QWord);
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "Q", 1L, RegistryValueKind.QWord);

        var snapshot = _handler.CaptureOriginal(action);
        _handler.Apply(action).IsSuccess.Should().BeTrue();
        ReadRawValue("Q").Should().Be(1L);

        _handler.Revert(action, snapshot.Value!).IsSuccess.Should().BeTrue();
        ReadRawValue("Q").Should().Be(9007199254740993L);
    }

    [Fact]
    public void BinaryRoundTrip_ShouldPreserveValue()
    {
        CreateTestKey();
        var original = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        WriteRawValue("B", original, RegistryValueKind.Binary);
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "B", new byte[] { 1, 2 }, RegistryValueKind.Binary);

        var snapshot = _handler.CaptureOriginal(action);
        _handler.Apply(action).IsSuccess.Should().BeTrue();
        ReadRawValue("B").Should().BeEquivalentTo(new byte[] { 1, 2 });

        _handler.Revert(action, snapshot.Value!).IsSuccess.Should().BeTrue();
        ReadRawValue("B").Should().BeEquivalentTo(original);
    }

    [Fact]
    public void MultiStringRoundTrip_ShouldPreserveValue()
    {
        CreateTestKey();
        WriteRawValue("M", new[] { "甲", "乙" }, RegistryValueKind.MultiString);
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "M", new[] { "丙" }, RegistryValueKind.MultiString);

        var snapshot = _handler.CaptureOriginal(action);
        _handler.Apply(action).IsSuccess.Should().BeTrue();
        ReadRawValue("M").Should().BeEquivalentTo(new[] { "丙" });

        _handler.Revert(action, snapshot.Value!).IsSuccess.Should().BeTrue();
        ReadRawValue("M").Should().BeEquivalentTo(new[] { "甲", "乙" });
    }

    [Fact]
    public void Verify_ShouldDetectExternalModification()
    {
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "V", 5, RegistryValueKind.DWord);
        _handler.Apply(action).IsSuccess.Should().BeTrue();
        _handler.Verify(action).IsSuccess.Should().BeTrue();

        // 模拟外部改动
        WriteRawValue("V", 6, RegistryValueKind.DWord);

        var verify = _handler.Verify(action);
        verify.IsSuccess.Should().BeFalse();
        verify.Error.Should().Contain("不一致");
    }

    [Fact]
    public void Revert_CorruptedSnapshot_ShouldFailWithClearError()
    {
        var action = new RegistrySetAction($"HKCU\\{_testKeyRelativePath}", "C", 1, RegistryValueKind.DWord);

        var revert = _handler.Revert(action, "{ 损坏的 JSON ");

        revert.IsSuccess.Should().BeFalse();
        revert.Error.Should().Contain("损坏");
    }

    [Fact]
    public void CaptureOriginal_UnknownHive_ShouldFail()
    {
        var action = new RegistrySetAction(@"HKXX\Software\pcopt-test", "V", 1, RegistryValueKind.DWord);

        var snapshot = _handler.CaptureOriginal(action);

        snapshot.IsSuccess.Should().BeFalse();
        snapshot.Error.Should().Contain("无法识别的注册表根键");
    }

    // ---------- 合规红线 #1：GPU 类注册表键只读 ----------

    [Theory]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2704\0000000")]
    [InlineData(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\PCI\VEN_1002&DEV_7470")]
    public void Apply_GpuClassPaths_ShouldBeRejected(string gpuPath)
    {
        var action = new RegistrySetAction(gpuPath, "HardwareInformation", "fake", RegistryValueKind.String);

        var snapshot = _handler.CaptureOriginal(action);
        snapshot.IsSuccess.Should().BeFalse();
        snapshot.Error.Should().Contain("只读");

        var apply = _handler.Apply(action);
        apply.IsSuccess.Should().BeFalse();
        apply.Error.Should().Contain("只读");

        var revert = _handler.Revert(action, "{}");
        revert.IsSuccess.Should().BeFalse();
        revert.Error.Should().Contain("只读");
    }

    [Fact]
    public void Apply_IfeoPath_ShouldBeAllowed()
    {
        var ifeoPath = IfeoPaths.PerfOptions("cs2.exe");

        var guard = PcOpt.Core.Compliance.ComplianceGuard.EnsureWritable(ifeoPath);

        guard.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CanHandle_ShouldOnlyAcceptRegistrySetAction()
    {
        _handler.CanHandle(new RegistrySetAction("HKCU\\X", "Y", 1, RegistryValueKind.DWord)).Should().BeTrue();
        _handler.CanHandle(new ShellAction("cmd.exe", "")).Should().BeFalse();
    }

    private void CreateTestKey()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(_testKeyRelativePath);
        _ = key;
    }

    private void WriteRawValue(string name, object value, RegistryValueKind kind)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(_testKeyRelativePath);
        key.SetValue(name, value, kind);
    }

    private object? ReadRawValue(string name)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(_testKeyRelativePath);
        return key?.GetValue(name);
    }

    private bool TestKeyExists()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        return baseKey.OpenSubKey(_testKeyRelativePath) is not null;
    }
}
