// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 产品 C 资源路径解析（随包 PresentMon、用户数据目录、兄弟产品可执行文件）。
/// </summary>
public static class ResourcePaths
{
    /// <summary>产品名（Journal / 用户配置归属）。</summary>
    public const string ProductName = "FrameLab";

    /// <summary>随包 PresentMon 目录名（部署时与产品同目录）。</summary>
    public const string ThirdPartyDirectoryName = "PresentMon";

    /// <summary>PresentMon 可执行文件名。</summary>
    public const string PresentMonExecutableName = "PresentMon.exe";

    /// <summary>用户数据目录：%AppData%\pcopt\FrameLab。</summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pcopt",
        ProductName);

    /// <summary>日志目录：%LocalAppData%\pcopt\FrameLab\logs。</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pcopt",
        ProductName,
        "logs");

    /// <summary>采集临时目录：%LocalAppData%\pcopt\FrameLab\captures。</summary>
    public static string CapturesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pcopt",
        ProductName,
        "captures");

    /// <summary>导出目录：我的文档\FrameLab（报告 CSV / PNG 默认落点）。</summary>
    public static string ExportsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ProductName);

    /// <summary>
    /// 查找随包的 PresentMon 可执行文件（依次在程序目录、兄弟目录、源码树探测）。
    /// 未找到返回 null（调用方据此给出"如何获取"的人话提示并优雅降级）。
    /// </summary>
    public static string? FindPresentMonExecutable()
    {
        var baseDirectory = AppContext.BaseDirectory;

        // 1) 程序目录下 ThirdParty\PresentMon\
        var direct = Path.Combine(baseDirectory, ThirdPartyDirectoryName, PresentMonExecutableName);
        if (File.Exists(direct))
        {
            return direct;
        }

        DirectoryInfo? directory = new(baseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++, directory = directory.Parent)
        {
            // 2) 探测点下 ThirdParty\PresentMon\（覆盖源码树根 / 输出上级）
            var candidate = Path.Combine(directory.FullName, "ThirdParty", "PresentMon", PresentMonExecutableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // 3) 直接同名文件
            var sibling = Path.Combine(directory.FullName, PresentMonExecutableName);
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        return null;
    }

    /// <summary>
    /// 查找某个兄弟产品的可执行文件（C 拉起 A 用）。
    /// 依次在程序目录、程序目录的兄弟目录、以及源码树标准输出目录中探测。
    /// </summary>
    /// <param name="executableName">可执行文件名（如 GameModePilot.exe）。</param>
    public static string? FindSiblingExecutable(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDirectory, executableName);
        if (File.Exists(direct))
        {
            return direct;
        }

        DirectoryInfo? directory = new(baseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++, directory = directory.Parent)
        {
            var sibling = Path.Combine(directory.FullName, executableName);
            if (File.Exists(sibling))
            {
                return sibling;
            }

            var productsRoot = Path.Combine(directory.FullName, "src", "Products");
            if (Directory.Exists(productsRoot))
            {
                var hit = Directory.EnumerateFiles(productsRoot, executableName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (hit is not null)
                {
                    return hit;
                }
            }
        }

        return null;
    }
}
