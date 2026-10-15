// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>垃圾清理"零误删"测试（T28，PRD B-P0-8 / 验收"零误删"）。</summary>
public sealed class JunkCleanupServiceTests
{
    [Fact]
    public void Scan_ReportsFilesInsideCategory()
    {
        using var temp = new TempDirectory();
        var inside = temp.Combine("junk");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "a.tmp"), "hello");
        File.WriteAllText(Path.Combine(inside, "b.tmp"), "world!!");

        var service = new JunkCleanupService(
            NullLogger<JunkCleanupService>.Instance,
            new[] { new JunkCategory("test", "测试", inside, "*", 0) });

        var info = service.Scan().Should().ContainSingle().Subject;
        info.FileCount.Should().Be(2);
        info.SizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Clean_DeletesInsideButKeepsOutsideAndDirectory()
    {
        using var temp = new TempDirectory();
        var inside = temp.Combine("junk");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "a.tmp"), "hello");
        File.WriteAllText(Path.Combine(inside, "b.tmp"), "world");
        var outside = Path.Combine(temp.Path, "important.txt");
        File.WriteAllText(outside, "do-not-delete");

        var service = new JunkCleanupService(
            NullLogger<JunkCleanupService>.Instance,
            new[] { new JunkCategory("test", "测试", inside, "*", 0) });

        var result = service.Clean(new[] { "test" });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().BeGreaterThan(0);
        Directory.Exists(inside).Should().BeTrue("不应删除目录本身");
        Directory.GetFiles(inside).Should().BeEmpty("类别目录内的文件应被清理");
        File.Exists(outside).Should().BeTrue("类别目录外的文件绝不能被删除");
    }

    [Fact]
    public void Clean_UnknownKey_Fails()
    {
        using var temp = new TempDirectory();
        var service = new JunkCleanupService(NullLogger<JunkCleanupService>.Instance, Array.Empty<JunkCategory>());
        service.Clean(new[] { "missing" }).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Clean_MissingDirectory_IsNoOp()
    {
        var service = new JunkCleanupService(
            NullLogger<JunkCleanupService>.Instance,
            new[] { new JunkCategory("ghost", "不存在", Path.Combine(Path.GetTempPath(), "pcopt-not-exist-" + Guid.NewGuid().ToString("N")), "*", 0) });

        var result = service.Clean(new[] { "ghost" });
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    /// <summary>
    /// 零误删加固（QA 残余风险 #2）：类别目录若是重定向链接（junction / 符号链接），
    /// 绝不枚举、绝不清理——避免"沿 junction 删到目标目录内的文件"。
    /// </summary>
    [Fact]
    public void Clean_SkipsReparsePointDirectories_NoMisdelete()
    {
        using var temp = new TempDirectory();
        var real = temp.Combine("real");
        Directory.CreateDirectory(real);
        var precious = Path.Combine(real, "precious.txt");
        File.WriteAllText(precious, "must-survive");

        var link = temp.Combine("link");
        if (!TryCreateDirectoryReparsePoint(link, real))
        {
            // 环境不允许创建重定向链接（无开发者模式且无 mklink 权限）：跳过（不误报失败）
            return;
        }

        var service = new JunkCleanupService(
            NullLogger<JunkCleanupService>.Instance,
            new[] { new JunkCategory("link", "链接目录", link, "*", 0) });

        service.Scan().Should().ContainSingle().Which.FileCount.Should().Be(0);
        var result = service.Clean(new[] { "link" });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().Be(0);
        File.Exists(precious).Should().BeTrue("绝不沿 junction / 符号链接删到目标目录内的文件");
    }

    /// <summary>尝试创建指向目录的重定向链接：优先符号链接，失败回退 junction。成功返回 true。</summary>
    private static bool TryCreateDirectoryReparsePoint(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            if (IsReparsePoint(linkPath))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 无权限（未开启开发者模式）→ 尝试 junction
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "cmd.exe",
                $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
