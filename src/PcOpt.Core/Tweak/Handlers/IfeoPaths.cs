// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Tweak.Handlers;

/// <summary>
/// IFEO（Image File Execution Options）路径助手（PRD A-P0-6 / 架构 3.6）。
/// 命中内核级反作弊时，进程调度类操作切换为 IFEO 注册表方案；
/// IFEO 本质是 HKLM 注册表写入，复用 <see cref="RegistrySetActionHandler"/> 的快照 / 还原能力，
/// 不再单独实现 handler（避免审计面分散，架构 2 依赖规则 4）。
/// </summary>
public static class IfeoPaths
{
    /// <summary>IFEO 根键路径。</summary>
    public const string IfeoBasePath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    /// <summary>IFEO PerfOptions 子键名。</summary>
    public const string PerfOptionsSubKeyName = "PerfOptions";

    /// <summary>
    /// IFEO PerfOptions 的 <c>CpuPriorityClass</c> 取值编码（Win32 优先级 1-6，默认 3=Normal）。
    /// </summary>
    public static class CpuPriorityClassValues
    {
        /// <summary>1 = Idle。</summary>
        public const int Idle = 1;

        /// <summary>2 = Below Normal。</summary>
        public const int BelowNormal = 2;

        /// <summary>3 = Normal（默认）。</summary>
        public const int Normal = 3;

        /// <summary>4 = Above Normal。</summary>
        public const int AboveNormal = 4;

        /// <summary>5 = High。</summary>
        public const int High = 5;

        /// <summary>6 = Realtime。</summary>
        public const int Realtime = 6;
    }

    /// <summary>
    /// 构造 IFEO PerfOptions 完整路径：<c>HKLM\SOFTWARE\...\Image File Execution Options\{exe}\PerfOptions</c>。
    /// 传入完整路径时仅取文件名部分（防止规则误用）。
    /// </summary>
    public static string PerfOptions(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            throw new ArgumentException("可执行文件名不能为空", nameof(executableName));
        }

        var fileName = Path.GetFileName(executableName.Trim().Replace('/', '\\'));
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException($"无法从“{executableName}”解析可执行文件名", nameof(executableName));
        }

        return $@"{IfeoBasePath}\{fileName}\{PerfOptionsSubKeyName}";
    }
}
