// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Game;

/// <summary>进程创建事件信息（ETW 订阅层产出，与匹配逻辑解耦）。</summary>
/// <param name="ProcessId">进程 Id。</param>
/// <param name="ExecutablePath">可执行文件完整路径（内核事件提供）。</param>
/// <param name="CommandLine">命令行（可选，部分事件源不提供）。</param>
public sealed record ProcessEventInfo(int ProcessId, string ExecutablePath, string? CommandLine);

/// <summary>匹配结果（命中数据库中的游戏）。</summary>
/// <param name="Game">命中的游戏条目。</param>
/// <param name="ProcessId">进程 Id。</param>
/// <param name="ExecutablePath">可执行文件完整路径。</param>
/// <param name="MatchKind">匹配方式。</param>
public sealed record GameMatchResult(
    GameInfo Game,
    int ProcessId,
    string ExecutablePath,
    GameMatchKind MatchKind);

/// <summary>匹配方式（架构 3.4：executable 名 / SHA256）。</summary>
public enum GameMatchKind
{
    /// <summary>可执行文件名匹配（大小写 / 路径无关）。</summary>
    ExecutableName = 0,

    /// <summary>SHA256 哈希匹配（数据库提供哈希时启用；当前 schema 未启用）。</summary>
    Sha256,
}

/// <summary>
/// 进程匹配器（T8：与 ETW 会话解耦的可注入抽象）。
/// 实现必须零误触发优先：常见非游戏软件绝不命中（宁可漏，不可错）。
/// </summary>
public interface IProcessMatcher
{
    /// <summary>尝试匹配进程创建事件；未命中返回 null。</summary>
    GameMatchResult? Match(ProcessEventInfo info);
}

/// <summary>
/// 基于 <see cref="GameDatabase"/> 的进程匹配器（T8）。
/// 匹配规则：
/// <list type="bullet">
/// <item>取可执行文件名（去路径）→ 规范化（小写、去 .exe）→ 数据库 O(1) 查找；</item>
/// <item>大小写 / 路径差异完全容错（<c>D:\Games\cs2.exe</c> 与 <c>CS2.EXE</c> 同命中）；</item>
/// <item>SHA256 匹配为扩展点：数据库 schema 增加 sha256 字段后启用，当前仅名称匹配；</item>
/// <item>零误触发原则：数据库只收录确定可执行名的游戏；常见软件（浏览器 / IDE / 办公）不在库中，自然不命中。</item>
/// </list>
/// </summary>
public sealed class GameProcessMatcher : IProcessMatcher
{
    private readonly GameDatabase _database;

    /// <summary>初始化匹配器。</summary>
    /// <param name="database">游戏数据库。</param>
    public GameProcessMatcher(GameDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc/>
    public GameMatchResult? Match(ProcessEventInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (string.IsNullOrWhiteSpace(info.ExecutablePath))
        {
            return null;
        }

        var game = _database.FindByExecutable(info.ExecutablePath);
        return game is null
            ? null
            : new GameMatchResult(game, info.ProcessId, info.ExecutablePath, GameMatchKind.ExecutableName);
    }
}
