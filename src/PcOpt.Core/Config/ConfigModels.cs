// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Hardware;

namespace PcOpt.Core.Config;

/// <summary>
/// 用户配置架构版本登记处（架构 3.5）。
/// 导入配置前必须校验 schemaVersion 处于 <see cref="SupportedVersions"/>；
/// 对不认识的版本一律拒绝导入并给出人话提示（不猜测、不静默迁移）。
/// </summary>
public static class UserConfigSchema
{
    /// <summary>当前写入的架构版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>受支持的架构版本集合（旧版本兼容在此登记；当前仅 v1）。</summary>
    public static readonly IReadOnlyList<int> SupportedVersions = new[] { CurrentVersion };

    /// <summary>判断版本是否受支持。</summary>
    public static bool IsSupported(int version) => SupportedVersions.Contains(version);

    /// <summary>针对不受支持版本生成面向用户的中文错误信息。</summary>
    public static string DescribeUnsupported(int version) => version > CurrentVersion
        ? $"配置架构版本过新（v{version}），当前应用仅支持 v{CurrentVersion}，请升级应用后再导入"
        : $"不支持的配置架构版本：v{version}（当前支持 v{string.Join(" / ", SupportedVersions)}），请用导出该配置的应用版本导入";
}

/// <summary>
/// Profile 强度档（架构 3.5：gameId × hardwareTier → 强度档 + 开关组合）。
/// </summary>
public enum ProfileIntensity
{
    /// <summary>保守：仅应用低风险规则，追求稳定优先。</summary>
    Conservative = 0,

    /// <summary>均衡：默认档，性能与稳定平衡。</summary>
    Balanced,

    /// <summary>激进：应用全部适用规则，含 Notice 级（Danger 级永远需单独确认）。</summary>
    Aggressive,
}

/// <summary>
/// 优化 Profile 存储键：<c>gameId × hardwareTier</c>（架构 3.5）。
/// </summary>
public readonly record struct ProfileKey
{
    /// <summary>通配 GameId：<c>*</c> 表示适用于全部游戏。</summary>
    public const string GlobalGameId = "*";

    /// <summary>用游戏 Id 与硬件档位构造键。</summary>
    public ProfileKey(string gameId, HardwareTier? hardwareTier)
    {
        GameId = gameId;
        HardwareTier = hardwareTier;
    }

    /// <summary>游戏 Id（<see cref="GlobalGameId"/> = 全部游戏）。</summary>
    public string GameId { get; }

    /// <summary>硬件档位；null = 不限档位。</summary>
    public HardwareTier? HardwareTier { get; }

    /// <summary>全局默认键（<c>*</c> × 不限档位）。</summary>
    public static ProfileKey Global => new(GlobalGameId, null);

    /// <summary>与另一个键是否指向同一存储槽位（GameId 大小写不敏感）。</summary>
    public bool Matches(string gameId, HardwareTier? hardwareTier) =>
        string.Equals(GameId, gameId, StringComparison.OrdinalIgnoreCase) && HardwareTier == hardwareTier;
}

/// <summary>
/// 优化 Profile：某一游戏 × 某一硬件档位下的强度档与开关组合（架构 3.5）。
/// </summary>
public sealed record OptimizationProfile
{
    /// <summary>适用游戏 Id；<see cref="ProfileKey.GlobalGameId"/> = 全部游戏。</summary>
    public string GameId { get; init; } = ProfileKey.GlobalGameId;

    /// <summary>适用硬件档位；null = 不限档位（按查找优先级回退，见 <see cref="UserConfig.FindProfile"/>）。</summary>
    public HardwareTier? HardwareTier { get; init; }

    /// <summary>强度档。</summary>
    public ProfileIntensity Intensity { get; init; } = ProfileIntensity.Balanced;

    /// <summary>开关组合：启用的规则分类（空 = 跟随强度档默认分类集）。</summary>
    public IReadOnlyList<string> EnabledCategories { get; init; } = Array.Empty<string>();

    /// <summary>开关组合-例外：强制禁用的规则 Id（优先级高于 EnabledCategories）。</summary>
    public IReadOnlyList<string> DisabledRuleIds { get; init; } = Array.Empty<string>();

    /// <summary>开关组合-追加：额外启用的规则 Id（用于把默认关闭的规则加入本 Profile）。</summary>
    public IReadOnlyList<string> ExtraRuleIds { get; init; } = Array.Empty<string>();

    /// <summary>存储键。</summary>
    public ProfileKey Key => new(GameId, HardwareTier);

    /// <summary>人话摘要（导入预览与界面列表用，不参与持久化）。</summary>
    public string Describe()
    {
        var scope = GameId == ProfileKey.GlobalGameId ? "全部游戏" : GameId;
        var tier = HardwareTier?.ToString() ?? "不限档位";
        return $"{scope} × {tier}：{Intensity}";
    }
}

/// <summary>用户偏好设置（随用户配置存储）。</summary>
public sealed record UserPreferences
{
    /// <summary>开机自启动（默认关，需用户在设置中显式开启）。</summary>
    public bool AutoStartWithWindows { get; init; }

    /// <summary>启动后最小化到托盘（默认关）。</summary>
    public bool StartMinimizedToTray { get; init; }

    /// <summary>启动时检查更新（默认关，PRD：更新默认关闭）。</summary>
    public bool CheckUpdatesOnStartup { get; init; }

    /// <summary>主题：System | Light | Dark。</summary>
    public string Theme { get; init; } = "System";

    /// <summary>Danger 级规则执行前弹确认框（默认开，PRD 5.5 高危确认规范）。</summary>
    public bool ConfirmBeforeDangerRules { get; init; } = true;
}

/// <summary>
/// 用户配置（架构 3.5：%AppData%\pcopt\{product}\config.json）。
/// schemaVersion 版本化；导入前必须通过 <see cref="UserConfigJson"/> schema 校验。
/// </summary>
public sealed record UserConfig
{
    /// <summary>构造默认配置（空 Profile + 默认偏好）。</summary>
    public UserConfig()
    {
        SchemaVersion = UserConfigSchema.CurrentVersion;
        Product = string.Empty;
    }

    /// <summary>架构版本（导入时校验的第一道关口）。</summary>
    public int SchemaVersion { get; init; }

    /// <summary>产品名（GameModePilot / GameOptBox / FrameLab）。</summary>
    public string Product { get; init; }

    /// <summary>Profile 集合（按 gameId × hardwareTier 唯一）。</summary>
    public IReadOnlyList<OptimizationProfile> Profiles { get; init; } = Array.Empty<OptimizationProfile>();

    /// <summary>用户偏好。</summary>
    public UserPreferences Preferences { get; init; } = new();

    /// <summary>
    /// 按 gameId × hardwareTier 查找 Profile，按以下优先级回退：
    /// 精确 (gameId, tier) → 全局 (*, tier) → 游戏默认 (gameId, null) → 全局默认 (*, null)。
    /// </summary>
    public OptimizationProfile? FindProfile(string gameId, HardwareTier hardwareTier)
    {
        var candidates = new[]
        {
            (GameId: gameId, Tier: (HardwareTier?)hardwareTier),
            (GameId: ProfileKey.GlobalGameId, Tier: (HardwareTier?)hardwareTier),
            (GameId: gameId, Tier: (HardwareTier?)null),
            (GameId: ProfileKey.GlobalGameId, Tier: (HardwareTier?)null),
        };

        foreach (var candidate in candidates)
        {
            foreach (var profile in Profiles)
            {
                if (profile.Key.Matches(candidate.GameId, candidate.Tier))
                {
                    return profile;
                }
            }
        }

        return null;
    }

    /// <summary>用另一个配置的 Profile 集合覆盖本配置中同键条目（导入合并用）。</summary>
    public UserConfig MergeProfiles(IReadOnlyList<OptimizationProfile> incoming)
    {
        var merged = new List<OptimizationProfile>(Profiles);
        foreach (var profile in incoming)
        {
            var index = merged.FindIndex(p => p.Key.Matches(profile.GameId, profile.HardwareTier));
            if (index >= 0)
            {
                merged[index] = profile;
            }
            else
            {
                merged.Add(profile);
            }
        }

        return this with { Profiles = merged };
    }
}

/// <summary>导入预览中单个条目的处置动作。</summary>
public enum ConfigImportAction
{
    /// <summary>新增（当前配置中不存在同键 Profile）。</summary>
    Add,

    /// <summary>更新（同键 Profile 内容有差异，将被覆盖）。</summary>
    Update,

    /// <summary>无变化（同键 Profile 内容一致，跳过）。</summary>
    Unchanged,

    /// <summary>拒绝（该条目本身不合法；此时整个导入无效）。</summary>
    Reject,
}

/// <summary>导入预览条目（逐项预览，PRD：导入前让用户看清每一条的影响）。</summary>
public sealed record ConfigImportItem
{
    /// <summary>处置动作。</summary>
    public ConfigImportAction Action { get; init; }

    /// <summary>条目存储键。</summary>
    public ProfileKey Key { get; init; }

    /// <summary>人话摘要（如"无畏契约 × Ultra：Balanced → Aggressive"）。</summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// 配置导入预览（架构 3.5：导入前 schema 校验 + 逐项预览）。
/// </summary>
public sealed record ConfigImportPreview
{
    /// <summary>配置是否通过 schema 校验（false 时 <see cref="Config"/> 为 null，禁止导入）。</summary>
    public bool IsValid { get; init; }

    /// <summary>待导入配置的架构版本（校验失败时为 0）。</summary>
    public int SchemaVersion { get; init; }

    /// <summary>逐项预览（按配置中的 Profile 顺序）。</summary>
    public IReadOnlyList<ConfigImportItem> Items { get; init; } = Array.Empty<ConfigImportItem>();

    /// <summary>错误信息集合（IsValid = false 时非空，直接展示给用户）。</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>校验通过的配置对象（导入执行时消费；校验失败为 null）。</summary>
    public UserConfig? Config { get; init; }

    /// <summary>有实际变化的条目（Add + Update）。</summary>
    public IReadOnlyList<ConfigImportItem> Changes =>
        Items.Where(item => item.Action is ConfigImportAction.Add or ConfigImportAction.Update).ToArray();
}
