// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>
/// 零误删"攻击性"验收测试（QA 独立构造，T30；PRD B-P0-8 "零误删"红线）：
/// 越界目录 / 相似名前缀目录 / 不可递归进入子目录 / 符号链接逃逸 / 占用中文件 / 目录本身不被删。
/// 这些用例并非工程师自报测试，而是独立构造以证伪"绝不误删用户数据"。
/// </summary>
public sealed class ZeroMisdeleteAttackTests
{
    private static JunkCleanupService Service(params JunkCategory[] categories) =>
        new(NullLogger<JunkCleanupService>.Instance, categories);

    [Fact]
    public void Clean_DoesNotRecurseIntoSubdirectories()
    {
        using var temp = new TempDirectory();
        var categoryDir = temp.Combine("user-temp");
        Directory.CreateDirectory(categoryDir);
        File.WriteAllText(Path.Combine(categoryDir, "top.tmp"), "top-level");
        var subDir = Path.Combine(categoryDir, "nested");
        Directory.CreateDirectory(subDir);
        var nestedFile = Path.Combine(subDir, "keep-me.txt");
        File.WriteAllText(nestedFile, "重要：子目录内文件绝不能被删");

        var service = Service(new JunkCategory("t", "测试", categoryDir, "*", 0));
        var result = service.Clean(new[] { "t" });

        result.IsSuccess.Should().BeTrue(result.Error);
        File.Exists(nestedFile).Should().BeTrue("清理不得递归进入子目录，子目录内文件必须保留");
        Directory.Exists(subDir).Should().BeTrue("子目录本身不得被删除");
        File.Exists(Path.Combine(categoryDir, "top.tmp")).Should().BeFalse("类别目录内的顶层文件应被清理");
    }

    [Fact]
    public void Clean_SiblingDirectoryWithSimilarPrefix_IsUntouched()
    {
        using var temp = new TempDirectory();
        var target = temp.Combine("cache");
        var sibling = temp.Combine("cache_backup"); // 前缀相似（"cache" 是 "cache_backup" 的前缀）
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(target, "a.tmp"), "junk");
        var siblingFile = Path.Combine(sibling, "precious.dat");
        File.WriteAllText(siblingFile, "重要：相似名目录绝不能被误删");

        var service = Service(new JunkCategory("c", "缓存", target, "*", 0));
        service.Clean(new[] { "c" }).IsSuccess.Should().BeTrue();

        File.Exists(siblingFile).Should().BeTrue("名字相似的相邻目录绝不能被清理");
        File.Exists(Path.Combine(target, "a.tmp")).Should().BeFalse();
    }

    [Fact]
    public void Clean_LockedFile_IsSkipped_OthersStillRemoved()
    {
        using var temp = new TempDirectory();
        var dir = temp.Combine("temp");
        Directory.CreateDirectory(dir);
        var locked = Path.Combine(dir, "locked.tmp");
        var free = Path.Combine(dir, "free.tmp");
        File.WriteAllText(locked, "in-use");
        File.WriteAllText(free, "removable");

        var service = Service(new JunkCategory("t", "测试", dir, "*", 0));

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = service.Clean(new[] { "t" });
            result.IsSuccess.Should().BeTrue("占用文件应被跳过而非导致整体失败");
        }

        File.Exists(locked).Should().BeTrue("占用中的文件不得被强制删除");
        File.Exists(free).Should().BeFalse("未占用的文件仍应被清理");
    }

    [Fact]
    public void Clean_SymlinkToExternalFile_DoesNotDeleteTarget()
    {
        using var temp = new TempDirectory();
        var dir = temp.Combine("temp");
        Directory.CreateDirectory(dir);
        var outsideTarget = Path.Combine(temp.Path, "outside-important.txt");
        File.WriteAllText(outsideTarget, "外部重要文件");
        var link = Path.Combine(dir, "link.txt");

        try
        {
            File.CreateSymbolicLink(link, outsideTarget);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 无创建符号链接权限（未开启开发者模式 / 无特权）：平台限制，非产品缺陷，跳过。
            return;
        }

        var service = Service(new JunkCategory("t", "测试", dir, "*", 0));
        service.Clean(new[] { "t" }).IsSuccess.Should().BeTrue();

        File.Exists(outsideTarget).Should().BeTrue("符号链接指向的外部文件绝不能被删除");
        File.Exists(link).Should().BeFalse("指向外部的链接本身可被清理，但不得跟随删除目标");
    }

    [Fact]
    public void Clean_DoesNotDeleteCategoryDirectoryItself()
    {
        using var temp = new TempDirectory();
        var dir = temp.Combine("temp");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "x.tmp"), "x");

        var service = Service(new JunkCategory("t", "测试", dir, "*", 0));
        service.Clean(new[] { "t" });

        Directory.Exists(dir).Should().BeTrue("类别目录本身必须保留");
        Directory.Exists(Path.Combine(dir, "sub")).Should().BeTrue();
    }

    [Fact]
    public void Clean_MinAgeDays_SkipsRecentFiles()
    {
        using var temp = new TempDirectory();
        var dir = temp.Combine("temp");
        Directory.CreateDirectory(dir);
        var fresh = Path.Combine(dir, "fresh.tmp");
        File.WriteAllText(fresh, "刚写入");

        var service = Service(new JunkCategory("t", "测试", dir, "*", 1)); // 只清 1 天前的
        var result = service.Clean(new[] { "t" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0, "未达到最小年龄的文件不应被清理");
        File.Exists(fresh).Should().BeTrue("新鲜文件必须在最小年龄之外才可清理");
    }

    [Fact]
    public void Clean_DirectoryIsActuallyAFile_IsNoOp()
    {
        using var temp = new TempDirectory();
        var bogus = Path.Combine(temp.Path, "not-a-dir");
        File.WriteAllText(bogus, "我是一个文件，不是目录");

        var service = Service(new JunkCategory("t", "测试", bogus, "*", 0));
        var result = service.Clean(new[] { "t" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        File.Exists(bogus).Should().BeTrue("把文件误当作类别目录时绝不能被删除");
    }

    // ---------------- Appx 分级：System 不可卸载（不可绕过） ----------------

    [Theory]
    [InlineData("Microsoft.WindowsAppRuntime.Microsoft.MicrosoftSolitaireCollection")] // 同时命中 System 与 Safe → System 优先
    [InlineData("Microsoft.MicrosoftSolitaireCollection.Microsoft.WindowsStore")]
    [InlineData("Microsoft.NET.Native.Microsoft.BingNews")]
    public void Grade_SystemMarkerTakesPrecedenceOverSafeMarker(string name)
    {
        AppxRiskGrader.Grade(name, null).Should().Be(AppxRiskLevel.System,
            "命中系统组件标记时，即使同时命中可安全卸载标记也必须判为系统组件");
    }

    [Theory]
    [InlineData("MICROSOFT.WINDOWSSTORE")]
    [InlineData("microsoft.vclibs.140.00")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.Windows.ShellExperienceHost")]
    [InlineData("Microsoft.Windows.StartMenuExperienceHost")]
    [InlineData("Microsoft.Windows.SecHealthUI")]
    public void Grade_SystemMarkers_AreCaseInsensitiveAndNonRemovable(string name)
    {
        var risk = AppxRiskGrader.Grade(name, null);
        risk.Should().Be(AppxRiskLevel.System);
        AppxRiskGrader.CanRemove(risk).Should().BeFalse("系统组件一律不可卸载");
    }

    [Fact]
    public void CanRemove_IsNeverTrueForSystemGrade()
    {
        // 覆盖全部已知系统组件标记，逐一验证"分级=System ⇒ 不可卸载"的不可绕过性。
        var systemNames = new[]
        {
            "Microsoft.VCLibs.140.00",
            "Microsoft.NET.Native.Framework.2.2",
            "Microsoft.WindowsAppRuntime.1.4",
            "Microsoft.UI.Xaml.2.8",
            "Microsoft.Services.Store.Engine",
            "Microsoft.WindowsStore",
            "Microsoft.DesktopAppInstaller",
            "Microsoft.Windows.ShellExperienceHost",
            "Microsoft.Windows.StartMenuExperienceHost",
            "Microsoft.Windows.SecHealthUI",
            "Microsoft.AAD.BrokerPlugin",
            "Microsoft.AccountsControl",
            "Microsoft.Windows.Search",
            "Microsoft.Windows.ContentDeliveryManager",
            "Microsoft.Windows.Cortana",
        };

        foreach (var name in systemNames)
        {
            var risk = AppxRiskGrader.Grade(name, null);
            risk.Should().Be(AppxRiskLevel.System, name);
            AppxRiskGrader.CanRemove(risk).Should().BeFalse(name);
        }
    }

    [Fact]
    public void Remove_SystemComponent_IsRejectedBeforeTouchingSystem()
    {
        // AppxPackageService.Remove 对不可卸载项必须在调用 PowerShell 之前拒绝（零误删纵深防御）。
        var service = new AppxPackageService(NullLogger<AppxPackageService>.Instance);
        var systemPackage = new AppxPackageInfo(
            "Microsoft.WindowsStore",
            "Microsoft.WindowsStore_22401.1400.3.0_x64__8wekyb3d8bbwe",
            "22401.1400.3.0",
            "Microsoft Corporation",
            AppxRiskLevel.System);

        var result = service.Remove(systemPackage);

        result.IsSuccess.Should().BeFalse("系统组件必须被服务层拒绝卸载");
        result.Error.Should().Contain("系统组件");
        systemPackage.CanRemove.Should().BeFalse();
    }
}
