// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Game;

/// <summary>卸载注册表条目（只读快照，供 GOG / Xbox / WeGame 保守探测）。</summary>
/// <param name="KeyName">注册表键名（平台内唯一 Id）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Publisher">发行商。</param>
/// <param name="InstallLocation">安装目录（可能为空）。</param>
/// <param name="UrlInfoAbout">关于链接（发行商标记的辅助判据）。</param>
public sealed record UninstallEntry(
    string KeyName,
    string? DisplayName,
    string? Publisher,
    string? InstallLocation,
    string? UrlInfoAbout);

/// <summary>
/// 卸载注册表读取器抽象（T7 保守探测的可注入点）。
/// 实现必须只读；单测通过替身注入伪造条目，不触碰真实注册表。
/// </summary>
public interface IUninstallRegistryReader
{
    /// <summary>读取全部卸载条目（HKLM + HKCU 的 Uninstall 键）。</summary>
    IReadOnlyList<UninstallEntry> ReadEntries();
}

/// <summary>
/// 游戏源路径抽象（T7：文件类平台根目录的可注入点）。
/// 实现从注册表 / VDF 解析真实路径；单测注入伪造目录树。
/// </summary>
public interface IGameSourcePaths
{
    /// <summary>Steam 安装目录（注册表 InstallPath）；未安装为 null。</summary>
    string? SteamInstallPath { get; }

    /// <summary>额外的 Steam 库目录（测试注入 / 用户手动配置；不猜路径）。</summary>
    IReadOnlyList<string> ExtraSteamLibraryPaths { get; }

    /// <summary>Epic 清单目录（默认 %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests）；未安装为 null。</summary>
    string? EpicManifestsDirectory { get; }
}

/// <summary>固定路径的游戏源（测试 / 手动配置用）。</summary>
public sealed class StaticGameSourcePaths : IGameSourcePaths
{
    /// <summary>初始化固定路径源。</summary>
    /// <param name="steamInstallPath">Steam 安装目录。</param>
    /// <param name="extraSteamLibraryPaths">额外 Steam 库目录。</param>
    /// <param name="epicManifestsDirectory">Epic 清单目录。</param>
    public StaticGameSourcePaths(
        string? steamInstallPath = null,
        IReadOnlyList<string>? extraSteamLibraryPaths = null,
        string? epicManifestsDirectory = null)
    {
        SteamInstallPath = steamInstallPath;
        ExtraSteamLibraryPaths = extraSteamLibraryPaths ?? Array.Empty<string>();
        EpicManifestsDirectory = epicManifestsDirectory;
    }

    /// <inheritdoc/>
    public string? SteamInstallPath { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> ExtraSteamLibraryPaths { get; }

    /// <inheritdoc/>
    public string? EpicManifestsDirectory { get; }
}
