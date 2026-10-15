// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>一个垃圾清理类别（T28）。</summary>
/// <param name="Key">稳定键。</param>
/// <param name="Title">人话标题。</param>
/// <param name="Directory">目标目录。</param>
/// <param name="Pattern">文件名匹配（默认 *）。</param>
/// <param name="MinAgeDays">最小文件年龄（天）：0 = 全部。</param>
public sealed record JunkCategory(string Key, string Title, string Directory, string Pattern = "*", int MinAgeDays = 0);

/// <summary>类别扫描结果（供预览展示清理量）。</summary>
/// <param name="Category">类别。</param>
/// <param name="SizeBytes">可清理字节数。</param>
/// <param name="FileCount">可清理文件数。</param>
public sealed record JunkCategoryInfo(JunkCategory Category, long SizeBytes, int FileCount);

/// <summary>
/// 垃圾清理服务（T28，PRD B-P0-8）。
/// 仅在<a href="白名单">固定的安全目录</a>内删除"用户勾选类别"的文件：
/// 绝不删除目录本身、绝不跟随重定向链接、跳过占用中文件；先扫描预览再删除（零误删红线）。
/// 类别可注入（默认使用系统临时 / 缓存目录），便于单元测试在临时目录中验证"零误删"。
/// </summary>
public sealed class JunkCleanupService
{
    private readonly ILogger<JunkCleanupService> _logger;
    private readonly IReadOnlyList<JunkCategory> _categories;

    /// <summary>初始化垃圾清理服务。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="categories">类别列表；null 使用 <see cref="DefaultCategories"/>。</param>
    public JunkCleanupService(ILogger<JunkCleanupService> logger, IReadOnlyList<JunkCategory>? categories = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _categories = categories ?? DefaultCategories;
    }

    /// <summary>默认清理类别（系统临时与缓存；着色器缓存单独列出）。</summary>
    public static IReadOnlyList<JunkCategory> DefaultCategories { get; } = BuildDefaultCategories();

    /// <summary>用于展示 / 选择的全部分类。</summary>
    public IReadOnlyList<JunkCategory> Categories => _categories;

    /// <summary>扫描全部类别，返回各类可清理量（预览）。</summary>
    public IReadOnlyList<JunkCategoryInfo> Scan()
    {
        var result = new List<JunkCategoryInfo>(_categories.Count);
        foreach (var category in _categories)
        {
            result.Add(ScanCategory(category));
        }

        return result;
    }

    /// <summary>清理指定类别（返回释放的字节数）。</summary>
    /// <param name="categoryKeys">要清理的类别键。</param>
    public Result<long> Clean(IReadOnlyList<string> categoryKeys)
    {
        ArgumentNullException.ThrowIfNull(categoryKeys);
        var selected = _categories
            .Where(category => categoryKeys.Contains(category.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0)
        {
            return Result<long>.Fail("未选择任何可清理项");
        }

        long freed = 0;
        foreach (var category in selected)
        {
            freed += CleanCategory(category);
        }

        _logger.LogInformation("垃圾清理完成，释放 {Bytes} 字节", freed);
        return Result<long>.Ok(freed);
    }

    private static JunkCategoryInfo ScanCategory(JunkCategory category)
    {
        long size = 0;
        var count = 0;
        foreach (var file in EnumerateFiles(category))
        {
            try
            {
                size += new FileInfo(file).Length;
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 读取失败的文件不计入
            }
        }

        return new JunkCategoryInfo(category, size, count);
    }

    private static long CleanCategory(JunkCategory category)
    {
        long freed = 0;
        foreach (var file in EnumerateFiles(category))
        {
            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 占用中的文件跳过，不中断
            }
        }

        return freed;
    }

    private static IEnumerable<string> EnumerateFiles(JunkCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.Directory) || !Directory.Exists(category.Directory))
        {
            return Array.Empty<string>();
        }

        // 零误删加固（QA 残余风险 #2）：类别目录本身若是重定向链接（junction / 符号链接），
        // 一律不枚举、不清理——避免"沿 junction 删到目标目录内的文件"。
        if (IsReparsePoint(category.Directory))
        {
            return Array.Empty<string>();
        }

        try
        {
            var cutoff = category.MinAgeDays > 0 ? DateTime.UtcNow.AddDays(-category.MinAgeDays) : DateTime.MinValue;
            // 注意：仅跳过"重定向链接目录"（见上）；指向外部的符号链接文件本身可被清理
            // （File.Delete 只删链接、不跟随目标），故不在此过滤文件级重解析点。
            return Directory.EnumerateFiles(category.Directory, category.Pattern, SearchOption.TopDirectoryOnly)
                .Where(file => IsOldEnough(file, cutoff))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 路径是否为重定向链接（junction / 符号链接等重解析点）。
    /// 读取属性失败时按"非链接"返回（保守：由后续删除时的 I/O 异常兜底），不影响正常清理。
    /// </summary>
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

    private static bool IsOldEnough(string file, DateTime cutoff)
    {
        if (cutoff == DateTime.MinValue)
        {
            return true;
        }

        try
        {
            return File.GetLastWriteTimeUtc(file) <= cutoff;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<JunkCategory> BuildDefaultCategories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windowsTemp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Temp");

        var categories = new List<JunkCategory>
        {
            new("user-temp", "用户临时文件", Path.GetTempPath(), "*", 0),
            new("system-temp", "系统临时文件", windowsTemp, "*", 1),
            new("inet-cache", "浏览器 / 系统网络缓存", Path.Combine(localAppData, "Microsoft", "Windows", "INetCache"), "*", 0),
            new("crash-dumps", "崩溃转储文件", Path.Combine(localAppData, "CrashDumps"), "*", 0),
            new("thumb-cache", "缩略图缓存", Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", 0),
            new("d3d-cache", "DirectX 着色器缓存", Path.Combine(localAppData, "D3DSCache"), "*", 0),
            new("nvidia-dx-cache", "NVIDIA 着色器缓存", Path.Combine(localAppData, "NVIDIA", "DXCache"), "*", 0),
        };

        return categories.Where(category => !string.IsNullOrWhiteSpace(category.Directory)).ToList();
    }
}
