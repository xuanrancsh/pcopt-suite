// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>启动项来源（T23，PRD B-P0-3：注册表 + 启动文件夹 + 计划任务三路）。</summary>
public enum StartupSource
{
    /// <summary>当前用户注册表 Run。</summary>
    RegistryCurrentUser,

    /// <summary>本机注册表 Run。</summary>
    RegistryLocalMachine,

    /// <summary>当前用户启动文件夹。</summary>
    StartupFolderUser,

    /// <summary>公共启动文件夹。</summary>
    StartupFolderCommon,

    /// <summary>计划任务。</summary>
    ScheduledTask,
}

/// <summary>一条启动项（T23）。</summary>
/// <param name="Id">稳定标识（跨刷新用于匹配）。</param>
/// <param name="Name">名称（注册表值名 / 文件名 / 任务名）。</param>
/// <param name="Command">命令行 / 可执行路径。</param>
/// <param name="Source">来源。</param>
/// <param name="Enabled">是否启用（禁用后可一键恢复）。</param>
/// <param name="Publisher">发布者（签名信息，尽力而为，取不到为空）。</param>
public sealed record StartupItem(
    string Id,
    string Name,
    string Command,
    StartupSource Source,
    bool Enabled,
    string Publisher)
{
    /// <summary>来源人话文案。</summary>
    public string SourceText => Source switch
    {
        StartupSource.RegistryCurrentUser => "当前用户注册表",
        StartupSource.RegistryLocalMachine => "本机注册表",
        StartupSource.StartupFolderUser => "用户启动文件夹",
        StartupSource.StartupFolderCommon => "公共启动文件夹",
        StartupSource.ScheduledTask => "计划任务",
        _ => "未知来源",
    };
}
