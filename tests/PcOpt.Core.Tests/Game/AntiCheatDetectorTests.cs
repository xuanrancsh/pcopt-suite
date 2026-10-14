// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Game;
using PcOpt.Core.Tests.TestDoubles;

namespace PcOpt.Core.Tests.Game;

/// <summary>
/// <see cref="AntiCheatDetector"/> 测试（T9）：
/// 本机无真实反作弊样本，全部用伪造快照模拟（架构 3.4 / 团队约束）。
/// </summary>
public sealed class AntiCheatDetectorTests
{
    private static AntiCheatDetectionResult Detect(FakeSecuritySnapshot snapshot) =>
        new AntiCheatDetector(snapshot).Detect();

    [Fact]
    public void Detect_NothingInstalled_ShouldBeNone()
    {
        // 零误操作原则：识别不到默认 None，绝不猜测
        var result = Detect(new FakeSecuritySnapshot(
            serviceNames: new[] { "SysMain", "Dnscache", "BEService-free-name" },
            driverFileNames: new[] { "ntfs.sys", "tcpip.sys" },
            processNames: new[] { "explorer", "chrome" }));

        result.Profile.Should().Be(AntiCheatProfile.None);
        result.Product.Should().Be(KnownAntiCheat.None);
        result.RequiresIfeoFallback.Should().BeFalse();
    }

    [Fact]
    public void Detect_VanguardService_ShouldBeKernel()
    {
        var result = Detect(new FakeSecuritySnapshot(serviceNames: new[] { "vgc", "vgk", "SysMain" }));

        result.Profile.Should().Be(AntiCheatProfile.Kernel);
        result.Product.Should().Be(KnownAntiCheat.Vanguard);
        result.RequiresIfeoFallback.Should().BeTrue();
        result.MatchedFeatures.Should().Contain(f => f.Value == "vgc");
    }

    [Fact]
    public void Detect_VanguardDriver_ShouldBeKernel()
    {
        var result = Detect(new FakeSecuritySnapshot(
            driverFileNames: new[] { "VGK.SYS", "ntfs.sys" }));

        // 大小写不敏感
        result.Profile.Should().Be(AntiCheatProfile.Kernel);
        result.Product.Should().Be(KnownAntiCheat.Vanguard);
    }

    [Theory]
    [InlineData("EasyAntiCheat.sys")]
    [InlineData("BEDaisy.sys")]
    [InlineData("Ricochet.sys")]
    [InlineData("RicochetAntiCheat.sys")]
    [InlineData("ACE-BASE.sys")]
    [InlineData("ACE-GAME.sys")]
    [InlineData("faceit.sys")]
    public void Detect_KnownKernelDrivers_ShouldBeKernel(string driverFileName)
    {
        var result = Detect(new FakeSecuritySnapshot(driverFileNames: new[] { driverFileName }));

        result.Profile.Should().Be(AntiCheatProfile.Kernel);
        result.Product.Should().NotBe(KnownAntiCheat.None);
    }

    [Theory]
    [InlineData("EasyAntiCheat")]
    [InlineData("BEDaisy")]
    [InlineData("BEService")]
    [InlineData("Ricochet")]
    [InlineData("AntiCheatExpert")]
    [InlineData("faceit")]
    public void Detect_KnownServices_ShouldBeKernel(string serviceName)
    {
        var result = Detect(new FakeSecuritySnapshot(serviceNames: new[] { serviceName }));

        result.Profile.Should().Be(AntiCheatProfile.Kernel);
    }

    [Fact]
    public void Detect_UserModeProcessOnly_ShouldBeUserMode()
    {
        // BattlEye 用户态进程单独出现（驱动未加载）：UserMode，不触发 IFEO 降级
        var result = Detect(new FakeSecuritySnapshot(processNames: new[] { "BEClient_x64" }));

        result.Profile.Should().Be(AntiCheatProfile.UserMode);
        result.Product.Should().Be(KnownAntiCheat.BattlEye);
        result.RequiresIfeoFallback.Should().BeFalse();
    }

    [Fact]
    public void Detect_KernelAndUserModeTogether_ShouldPreferKernel()
    {
        var result = Detect(new FakeSecuritySnapshot(
            serviceNames: new[] { "EasyAntiCheat" },
            processNames: new[] { "BEClient_x64" }));

        result.Profile.Should().Be(AntiCheatProfile.Kernel);
        result.Product.Should().Be(KnownAntiCheat.EasyAntiCheat);
    }

    [Fact]
    public void Detect_VacHasNoExternalFeatures_ShouldBeNone()
    {
        // VAC 纯用户态注入、无外部可观测特征：恒 None（设计决策见 AntiCheatSignatureLibrary）
        var signature = AntiCheatSignatureLibrary.Signatures.Single(s => s.Product == KnownAntiCheat.Vac);

        signature.KernelFeatures.Should().BeEmpty();
        signature.UserModeFeatures.Should().BeEmpty();
    }

    [Fact]
    public void Detect_SubstringNames_ShouldNotFalseTrigger()
    {
        // 精确匹配原则：含反作弊子串但不相等的服务名绝不命中
        var result = Detect(new FakeSecuritySnapshot(
            serviceNames: new[] { "MyEasyAntiCheatTool", "vgc-backup", "NotBEDaisy" },
            driverFileNames: new[] { "not-vgk.sys.bak", "vgk2.sys" }));

        result.Profile.Should().Be(AntiCheatProfile.None);
    }

    [Fact]
    public void WindowsSecuritySnapshot_OnRealMachine_ShouldNotThrow_AndReturnNonEmptyServices()
    {
        var snapshot = new WindowsSecuritySnapshot();

        var act = () => _ = snapshot.ServiceNames;

        act.Should().NotThrow();
        snapshot.ServiceNames.Should().NotBeEmpty("真实系统必有已安装服务");
    }

    [Fact]
    public void WindowsSecuritySnapshot_DriverExtraction_ShouldHandleNtPaths()
    {
        WindowsSecuritySnapshot.ExtractFileName(@"\SystemRoot\System32\drivers\vgk.sys")
            .Should().Be("vgk.sys");
        WindowsSecuritySnapshot.ExtractFileName(@"""C:\Program Files\App\app.exe""")
            .Should().Be("app.exe");
        WindowsSecuritySnapshot.ExtractFileName("ntfs.sys").Should().Be("ntfs.sys");
        WindowsSecuritySnapshot.ExtractFileName(null).Should().BeNull();
        WindowsSecuritySnapshot.ExtractFileName("  ").Should().BeNull();
    }
}
