// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Game;

/// <summary>游戏平台（架构 3.4：六大平台库扫描）。</summary>
public enum GamePlatform
{
    /// <summary>未知 / 手动添加。</summary>
    Unknown = 0,
    Steam,
    Epic,
    Gog,
    Xbox,
    WeGame,
}

/// <summary>游戏检测置信度（探测不到时归"手动添加"，不猜测）。</summary>
public enum GameDetectionConfidence
{
    /// <summary>平台清单文件确认（Steam appmanifest / Epic .item / GOG 注册表条目）。</summary>
    Confirmed = 0,

    /// <summary>注册表启发式推断（发行商标记匹配，未经平台清单确认）。</summary>
    Inferred,
}

/// <summary>
/// 已安装游戏（架构 3.4 库扫描产出，不可变记录）。
/// </summary>
/// <param name="GameId">平台内唯一 Id（Steam appid / Epic AppName / 注册表键名）。</param>
/// <param name="Name">游戏名（人话，来自平台清单）。</param>
/// <param name="Platform">平台。</param>
/// <param name="InstallPath">安装目录（真实存在；探测不到为 null，不猜路径）。</param>
/// <param name="ExecutableNames">可执行文件名集合（不含路径，ETW 匹配用；未知为空）。</param>
/// <param name="LaunchExecutable">启动可执行文件（相对安装目录；未知为 null）。</param>
/// <param name="Confidence">检测置信度。</param>
public sealed record InstalledGame(
    string GameId,
    string Name,
    GamePlatform Platform,
    string? InstallPath,
    IReadOnlyList<string> ExecutableNames,
    string? LaunchExecutable,
    GameDetectionConfidence Confidence);
