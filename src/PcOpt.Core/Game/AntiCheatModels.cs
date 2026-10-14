// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Core.Game;

/// <summary>
/// 反作弊画像（架构 3.4）。
/// 命中 Kernel 时 SessionEngine 自动降级为 IFEO 方案（A-P0-6 / PRD 红线 #5）。
/// </summary>
public enum AntiCheatProfile
{
    /// <summary>未识别到已知反作弊（零误操作原则：识别不到默认 None，绝不猜测）。</summary>
    None = 0,

    /// <summary>内核级反作弊（驱动已加载）：进程调度类操作必须降级 IFEO。</summary>
    Kernel,

    /// <summary>用户态反作弊（服务 / 进程在，但无内核驱动特征）。</summary>
    UserMode,
}

/// <summary>反作弊特征类型。</summary>
public enum AntiCheatFeatureKind
{
    /// <summary>驱动文件名（System32\drivers 或服务 ImagePath 中的文件名）。</summary>
    DriverFile = 0,

    /// <summary>服务名（注册表 Services 子键名）。</summary>
    ServiceName,

    /// <summary>进程名（运行中的可执行文件名）。</summary>
    ProcessName,
}

/// <summary>单条反作弊特征。</summary>
/// <param name="Kind">特征类型。</param>
/// <param name="Value">特征值（文件名 / 服务名 / 进程名，匹配时大小写不敏感）。</param>
public sealed record AntiCheatFeature(AntiCheatFeatureKind Kind, string Value)
{
    /// <summary>创建驱动文件特征。</summary>
    public static AntiCheatFeature Driver(string fileName) => new(AntiCheatFeatureKind.DriverFile, fileName);

    /// <summary>创建服务名特征。</summary>
    public static AntiCheatFeature Service(string serviceName) => new(AntiCheatFeatureKind.ServiceName, serviceName);

    /// <summary>创建进程名特征。</summary>
    public static AntiCheatFeature Process(string processName) => new(AntiCheatFeatureKind.ProcessName, processName);
}

/// <summary>反作弊产品签名（特征库条目）。</summary>
/// <param name="Product">产品标识。</param>
/// <param name="KernelFeatures">内核级特征（任一命中 → <see cref="AntiCheatProfile.Kernel"/>）。</param>
/// <param name="UserModeFeatures">用户态特征（仅在这些命中时 → <see cref="AntiCheatProfile.UserMode"/>）。</param>
/// <param name="Notes">说明（为何这样设计检测特征）。</param>
public sealed record AntiCheatSignature(
    KnownAntiCheat Product,
    IReadOnlyList<AntiCheatFeature> KernelFeatures,
    IReadOnlyList<AntiCheatFeature> UserModeFeatures,
    string Notes);

/// <summary>
/// 反作弊特征库（T9，覆盖 Vanguard / EAC / BattlEye / RICOCHET / TencentACE / FACEIT / VAC）。
/// 设计原则：
/// <list type="bullet">
/// <item>只收录外部可观测特征（服务 / 驱动文件 / 进程），不 hook、不读游戏进程内存（红线 #4）；</item>
/// <item>VAC 为纯用户态注入、无外部可观测特征：不设任何特征（恒不命中 → None）。
/// 这是有意设计——VAC 不拦截用户态进程调度操作，无需降级；</item>
/// <item>特征值为精确匹配（大小写不敏感），不用模糊子串，降低误报。</item>
/// </list>
/// </summary>
public static class AntiCheatSignatureLibrary
{
    /// <summary>全部签名。</summary>
    public static IReadOnlyList<AntiCheatSignature> Signatures { get; } = new[]
    {
        new AntiCheatSignature(
            KnownAntiCheat.Vanguard,
            new[]
            {
                AntiCheatFeature.Driver("vgk.sys"),
                AntiCheatFeature.Service("vgk"),
                AntiCheatFeature.Service("vgc"),
                AntiCheatFeature.Service("vgkService"),
            },
            Array.Empty<AntiCheatFeature>(),
            "Riot Vanguard：内核驱动 vgk.sys + 常驻服务 vgc/vgk"),

        new AntiCheatSignature(
            KnownAntiCheat.EasyAntiCheat,
            new[]
            {
                AntiCheatFeature.Driver("EasyAntiCheat.sys"),
                AntiCheatFeature.Service("EasyAntiCheat"),
            },
            new[] { AntiCheatFeature.Process("EasyAntiCheat") },
            "EAC：驱动 EasyAntiCheat.sys 随游戏启动；服务残留常见于卸载不净"),

        new AntiCheatSignature(
            KnownAntiCheat.BattlEye,
            new[]
            {
                AntiCheatFeature.Driver("BEDaisy.sys"),
                AntiCheatFeature.Service("BEDaisy"),
                AntiCheatFeature.Service("BEService"),
            },
            new[] { AntiCheatFeature.Process("BEClient_x64") },
            "BattlEye：内核驱动 BEDaisy.sys；BEService 为用户态服务（单独出现记 UserMode）"),

        new AntiCheatSignature(
            KnownAntiCheat.Ricochet,
            new[]
            {
                AntiCheatFeature.Driver("Ricochet.sys"),
                AntiCheatFeature.Driver("RicochetAntiCheat.sys"),
                AntiCheatFeature.Service("Ricochet"),
            },
            Array.Empty<AntiCheatFeature>(),
            "Activision Ricochet（CoD 系列）：驱动文件名在不同版本有差异，两个候选都收录"),

        new AntiCheatSignature(
            KnownAntiCheat.TencentAce,
            new[]
            {
                AntiCheatFeature.Driver("ACE-BASE.sys"),
                AntiCheatFeature.Driver("ACE-GAME.sys"),
                AntiCheatFeature.Service("AntiCheatExpert"),
            },
            Array.Empty<AntiCheatFeature>(),
            "腾讯 ACE：驱动 ACE-BASE.sys / ACE-GAME.sys；服务名 AntiCheatExpert"),

        new AntiCheatSignature(
            KnownAntiCheat.Faceit,
            new[]
            {
                AntiCheatFeature.Driver("faceit.sys"),
                AntiCheatFeature.Service("faceit"),
            },
            new[] { AntiCheatFeature.Process("FACEIT") },
            "FACEIT 反作弊：内核驱动 faceit.sys（平台对战客户端携带）"),

        new AntiCheatSignature(
            KnownAntiCheat.Vac,
            Array.Empty<AntiCheatFeature>(),
            Array.Empty<AntiCheatFeature>(),
            "Valve VAC 为纯用户态注入，无外部可观测特征（不 hook 游戏进程，红线 #4）：" +
            "恒不命中 → None。VAC 不拦截用户态进程调度操作，无需 IFEO 降级。"),
    };
}

/// <summary>反作弊检测结果。</summary>
/// <param name="Profile">检测到的画像。</param>
/// <param name="Product">命中的反作弊产品；未命中为 <see cref="KnownAntiCheat.None"/>。</param>
/// <param name="MatchedFeatures">命中的特征列表（供 UI 透明展示"为什么降级"）。</param>
public sealed record AntiCheatDetectionResult(
    AntiCheatProfile Profile,
    KnownAntiCheat Product,
    IReadOnlyList<AntiCheatFeature> MatchedFeatures)
{
    /// <summary>未命中结果（零误操作原则的默认值）。</summary>
    public static AntiCheatDetectionResult None { get; } = new(
        AntiCheatProfile.None,
        KnownAntiCheat.None,
        Array.Empty<AntiCheatFeature>());

    /// <summary>是否需要 IFEO 降级（内核级反作弊命中）。</summary>
    public bool RequiresIfeoFallback => Profile == AntiCheatProfile.Kernel;
}
