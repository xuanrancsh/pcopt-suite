// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 产品 B 资源路径解析（随包发布的签名规则包、用户数据目录、兄弟产品可执行文件）。
/// </summary>
public static class ResourcePaths
{
    /// <summary>产品名（Journal / 用户配置归属）。</summary>
    public const string ProductName = "GameOptBox";

    /// <summary>签名规则包目录相对路径（rules\tweaks）。</summary>
    public const string TweaksRelativeDirectory = @"rules\tweaks";

    /// <summary>规则包公钥文件名。</summary>
    public const string PublicKeyFileName = "public.key";

    /// <summary>用户数据目录：%AppData%\pcopt\GameOptBox。</summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pcopt",
        ProductName);

    /// <summary>日志目录：%LocalAppData%\pcopt\GameOptBox\logs。</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pcopt",
        ProductName,
        "logs");

    /// <summary>查找随包的规则包公钥（向上最多 6 层搜索 rules\public.key）。</summary>
    public static string? FindPublicKey() => FindUnderRules(PublicKeyFileName);

    /// <summary>查找随包的签名规则目录（向上最多 6 层搜索 rules\tweaks）。</summary>
    public static string? FindTweaksDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "rules", "tweaks");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 查找某个兄弟产品的可执行文件（产品 B 拉起 A/C 用）。
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
            // 同一 Release/Debug 输出目录下的兄弟产品
            var sibling = Path.Combine(directory.FullName, executableName);
            if (File.Exists(sibling))
            {
                return sibling;
            }

            // 源码树：src\Products\{产品}\bin\{配置}\{TFM}\{exe}
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

    private static string? FindUnderRules(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "rules", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
