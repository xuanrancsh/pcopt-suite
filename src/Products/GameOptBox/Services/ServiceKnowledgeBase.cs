// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 系统服务知识库（T22，PRD B-P0-1："服务说明覆盖率 100%"）。
/// 维护常见服务的<b>人话说明</b>与<b>推荐启动类型</b>；未知服务回退为通用说明（保证覆盖率 100%）。
/// 纯静态数据，便于单元测试。
/// </summary>
public static class ServiceKnowledgeBase
{
    private static readonly ServiceStartMode Keep = ServiceStartMode.Automatic;
    private static readonly ServiceStartMode Manual = ServiceStartMode.Manual;
    private static readonly ServiceStartMode Off = ServiceStartMode.Disabled;

    private static readonly Dictionary<string, ServiceKnowledge> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- 可关闭的后台 / 遥测类（性能） ----
        ["SysMain"] = new("预读取常用程序到内存以加快启动（SSD 上收益有限，可改手动）", Manual, true),
        ["WSearch"] = new("为文件内容建立搜索索引（改手动后系统搜索会变慢）", Manual, true),
        ["DiagTrack"] = new("连接的设备体验与遥测，向后台上报使用数据", Off, true),
        ["dmwappushservice"] = new("WAP 推送消息路由，普通用户用不到", Off, true),
        ["dmwappushsvc"] = new("WAP 推送消息路由，普通用户用不到", Off, true),
        ["WerSvc"] = new("Windows 错误报告，崩溃时向后台上传报告", Manual, true),
        ["Fax"] = new("传真功能，现代电脑基本用不到", Off, true),
        ["RemoteRegistry"] = new("允许远程读写本机注册表（存在安全风险）", Off, true),
        ["Spooler"] = new("打印后台处理，接打印机才需要", Manual, true),
        ["PrintNotify"] = new("打印通知扩展", Manual, true),
        ["bthserv"] = new("蓝牙支持服务，不用蓝牙可关", Manual, true),
        ["BTAGService"] = new("蓝牙音频网关，不用蓝牙耳机可关", Manual, true),
        ["Themes"] = new("桌面主题与视觉样式（关闭会影响外观）", Keep, false),
        ["TrkWks"] = new("分布式链接跟踪客户端", Manual, true),
        ["lltdsvc"] = new("链路层拓扑发现（家庭组遗留）", Manual, true),
        ["SNMPTRAP"] = new("SNMP 陷阱（企业环境用）", Manual, true),
        ["MapsBroker"] = new("离线地图下载管理", Manual, true),
        ["RetailDemo"] = new("零售演示模式（仅门店机用）", Off, true),
        ["WMPNetworkSvc"] = new("Windows Media Player 网络共享", Manual, true),
        ["XblAuthManager"] = new("Xbox 身份验证管理器", Manual, true),
        ["XblGameSave"] = new("Xbox 云存档", Manual, true),
        ["XboxNetApiSvc"] = new("Xbox Live 网络服务", Manual, true),
        ["XboxGipSvc"] = new("Xbox 配件管理", Manual, true),
        ["diagnosticshub.standardcollector.service"] = new("诊断中心标准收集器", Manual, true),
        ["DoSvc"] = new("传递优化（P2P 更新分发，占带宽）", Manual, true),
        ["WpnService"] = new("Windows 推送通知（通知栏需要它）", Keep, false),
        ["PcaSvc"] = new("程序兼容性助手（旧程序兼容检测）", Manual, true),

        // ---- 游戏 / 图形相关（游戏优化） ----
        ["GamingServices"] = new("Xbox 游戏服务运行时（部分微软商店游戏需要）", Keep, false),
        ["GamingServicesNet"] = new("Xbox 游戏服务网络组件", Manual, true),
        ["AMD External Events Utility"] = new("AMD 显卡事件服务（驱动相关，建议保留）", Keep, false),
        ["NVDisplay.ContainerLocalSystem"] = new("NVIDIA 显示容器服务（驱动相关，建议保留）", Keep, false),
        ["NvContainerLocalSystem"] = new("NVIDIA 驱动容器服务（建议保留）", Keep, false),
        ["igfxCUIService2.0.0.0"] = new("Intel 核显控制服务（建议保留）", Keep, false),

        // ---- 网络相关 ----
        ["SharedAccess"] = new("Internet 连接共享（给别的设备共享网络才需要）", Manual, true),
        ["icssvc"] = new("移动热点共享", Manual, true),
        ["SSDPSRV"] = new("SSDP 发现（UPnP 设备发现）", Manual, true),
        ["upnphost"] = new("UPnP 设备主机", Manual, true),
        ["Dnscache"] = new("DNS 客户端缓存（建议保留）", Keep, false),
        ["Dhcp"] = new("DHCP 客户端（获取 IP，必须保留）", Keep, false),

        // ---- 系统关键（勿关） ----
        ["RpcSs"] = new("远程过程调用（系统核心，不可关）", Keep, false),
        ["DcomLaunch"] = new("DCOM 服务器启动器（系统核心，不可关）", Keep, false),
        ["LSM"] = new("本地会话管理器（系统核心，不可关）", Keep, false),
        ["Power"] = new("电源管理（系统核心，不可关）", Keep, false),
        ["Winmgmt"] = new("WMI 服务（系统核心，不可关）", Keep, false),
        ["EventLog"] = new("Windows 事件日志（系统核心，不可关）", Keep, false),
        ["Schedule"] = new("任务计划程序（系统核心，不可关）", Keep, false),
        ["AudioSrv"] = new("音频服务（关掉就没声音）", Keep, false),
        ["Audiosrv"] = new("音频服务（关掉就没声音）", Keep, false),
        ["AudioEndpointBuilder"] = new("音频设备枚举（关掉就没声音）", Keep, false),
        ["BFE"] = new("基础筛选引擎（防火墙依赖，不可关）", Keep, false),
        ["mpssvc"] = new("Windows Defender 防火墙（建议保留）", Keep, false),
        ["WinDefend"] = new("Windows Defender 防病毒（建议保留）", Keep, false),
        ["wuauserv"] = new("Windows 更新", Keep, false),
        ["CryptSvc"] = new("加密服务（系统核心，不可关）", Keep, false),
        ["Themes2"] = new("主题服务", Keep, false),
        ["StorSvc"] = new("存储服务（系统核心，不可关）", Keep, false),
        ["UserManager"] = new("用户管理器（系统核心，不可关）", Keep, false),
        ["ProfSvc"] = new("用户配置文件服务（系统核心，不可关）", Keep, false),
    };

    /// <summary>已知服务数量。</summary>
    public static int KnownCount => Known.Count;

    /// <summary>
    /// 查询服务的人话说明与推荐值；未知服务回退为通用说明（保证 100% 覆盖率）。
    /// </summary>
    /// <param name="serviceName">服务名。</param>
    /// <param name="displayName">显示名（回退文案用）。</param>
    public static ServiceKnowledge Describe(string serviceName, string? displayName)
    {
        if (Known.TryGetValue(serviceName, out var knowledge))
        {
            return knowledge;
        }

        var label = string.IsNullOrWhiteSpace(displayName) ? serviceName : displayName;
        return new ServiceKnowledge(
            $"“{label}”系统服务（暂无人话说明，建议保持当前设置）",
            ServiceStartMode.Automatic,
            false);
    }

    /// <summary>是否收录了该服务的人话说明。</summary>
    public static bool IsKnown(string serviceName) => Known.ContainsKey(serviceName);
}
