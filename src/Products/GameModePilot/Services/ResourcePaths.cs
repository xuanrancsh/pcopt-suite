// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 产品 A 资源路径解析（随包发布的规则/数据库、用户数据目录）。
/// </summary>
public static class ResourcePaths
{
    /// <summary>游戏数据库相对路径（rules\games.yaml）。</summary>
    public const string GamesDatabaseRelativePath = @"rules\games.yaml";

    /// <summary>规则包公钥文件名。</summary>
    public const string PublicKeyFileName = "public.key";

    /// <summary>用户数据目录：%AppData%\pcopt\GameModePilot。</summary>
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pcopt",
        "GameModePilot");

    /// <summary>日志目录：%LocalAppData%\pcopt\GameModePilot\logs。</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pcopt",
        "GameModePilot",
        "logs");

    /// <summary>
    /// 查找随包的 games.yaml：从程序目录向上最多 6 层搜索 rules\games.yaml
    /// （兼容 Release 输出目录与开发期源码树）。
    /// </summary>
    public static string? FindGamesDatabase() => FindUnderRules("games.yaml");

    /// <summary>查找随包的规则公钥。</summary>
    public static string? FindPublicKey() => FindUnderRules(PublicKeyFileName);

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
