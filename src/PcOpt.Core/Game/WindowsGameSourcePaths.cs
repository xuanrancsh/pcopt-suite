// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json;
using Microsoft.Win32;

namespace PcOpt.Core.Game;

/// <summary>
/// Windows 真实游戏源路径（T7）：Steam 安装目录来自注册表（只读），
/// Epic 清单目录取 %ProgramData% 约定位置（存在才返回，不猜路径）。
/// </summary>
public sealed class WindowsGameSourcePaths : IGameSourcePaths
{
    /// <summary>Epic 清单目录（EpicGamesLauncher 全局数据，与登录用户无关）。</summary>
    private const string EpicManifestsRelativePath = @"Epic\EpicGamesLauncher\Data\Manifests";

    /// <inheritdoc/>
    public string? SteamInstallPath => ReadSteamInstallPath();

    /// <inheritdoc/>
    public IReadOnlyList<string> ExtraSteamLibraryPaths => Array.Empty<string>();

    /// <inheritdoc/>
    public string? EpicManifestsDirectory
    {
        get
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                return null;
            }

            var path = Path.Combine(programData, EpicManifestsRelativePath);
            return Directory.Exists(path) ? path : null;
        }
    }

    /// <summary>从注册表读取 Steam 安装目录（64 位视图优先，32 位回退；只读）。</summary>
    public static string? ReadSteamInstallPath()
    {
        foreach (var (hive, path) in new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam"),
        })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                var installPath = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
                {
                    return installPath;
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
            {
                // 注册表不可读：尝试下一个候选位置
            }
        }

        return null;
    }
}

/// <summary>
/// Windows 卸载注册表读取器（T7：GOG / Xbox / WeGame 保守探测，只读）。
/// 扫描 HKLM（64/32 视图）+ HKCU 的 Uninstall 键；读取失败返回空列表不抛。
/// </summary>
public sealed class WindowsUninstallRegistryReader : IUninstallRegistryReader
{
    private static readonly (RegistryHive Hive, string Path)[] UninstallRoots =
    {
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    /// <inheritdoc/>
    public IReadOnlyList<UninstallEntry> ReadEntries()
    {
        var entries = new List<UninstallEntry>();
        foreach (var (hive, path) in UninstallRoots)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (root is null)
                {
                    continue;
                }

                foreach (var keyName in root.GetSubKeyNames())
                {
                    using var entryKey = root.OpenSubKey(keyName);
                    if (entryKey is null)
                    {
                        continue;
                    }

                    entries.Add(new UninstallEntry(
                        keyName,
                        entryKey.GetValue("DisplayName") as string,
                        entryKey.GetValue("Publisher") as string,
                        entryKey.GetValue("InstallLocation") as string,
                        entryKey.GetValue("URLInfoAbout") as string));
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
            {
                // 单个根读取失败不影响其他根
            }
        }

        return entries;
    }
}

/// <summary>
/// Epic .item 清单 JSON 读取结果（T7：只取需要的字段）。
/// </summary>
/// <param name="AppName">应用唯一名（GameId）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="InstallLocation">安装目录。</param>
/// <param name="LaunchExecutable">启动可执行文件（相对安装目录；可能为空）。</param>
public sealed record EpicManifestItem(
    string AppName,
    string DisplayName,
    string? InstallLocation,
    string? LaunchExecutable);

/// <summary>Epic .item 清单解析（JSON，宽松读取：缺字段返回 null 而非抛）。</summary>
public static class EpicManifestReader
{
    /// <summary>解析 .item 清单文本。</summary>
    public static Result<EpicManifestItem> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<EpicManifestItem>.Fail("Epic 清单内容为空");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<EpicManifestItem>.Fail("Epic 清单不是 JSON 对象");
            }

            var appName = GetString(root, "AppName");
            if (string.IsNullOrWhiteSpace(appName))
            {
                return Result<EpicManifestItem>.Fail("Epic 清单缺少 AppName");
            }

            var displayName = GetString(root, "DisplayName") ?? appName;
            return Result<EpicManifestItem>.Ok(new EpicManifestItem(
                appName,
                displayName,
                GetString(root, "InstallLocation"),
                GetString(root, "LaunchExecutable")));
        }
        catch (JsonException ex)
        {
            return Result<EpicManifestItem>.Fail($"Epic 清单 JSON 无效：{ex.Message}");
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
