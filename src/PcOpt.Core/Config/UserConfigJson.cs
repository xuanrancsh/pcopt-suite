// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using System.Text.Json;
using PcOpt.Core.Hardware;

namespace PcOpt.Core.Config;

/// <summary>
/// 用户配置 JSON 序列化与 schema 校验（架构 3.5）。
/// 校验策略（导入前第一道关口，全部拒绝均给出中文人话原因）：
/// 1) 根节点必须是 JSON 对象；2) schemaVersion 必填且为受支持整数；
/// 3) product 必填非空；4) profiles 逐项校验（gameId 必填、hardwareTier/intensity 可空但出现时必须合法、
///    字符串列表元素非空、gameId × hardwareTier 键唯一）；5) preferences 逐字段校验（theme 白名单、布尔类型）。
/// 实现采用 JsonDocument 手工读取而非宽松反序列化：宁可拒绝也不猜测（合规红线）。
/// </summary>
public static class UserConfigJson
{
    /// <summary>theme 字段合法取值白名单。</summary>
    private static readonly string[] AllowedThemes = { "System", "Light", "Dark" };

    /// <summary>序列化配置为 JSON 文本（缩进 2 空格，UTF-8 可直接落盘）。</summary>
    public static string Serialize(UserConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteConfig(writer, config);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>反序列化并严格校验（坏包在此拒绝，调用方拿到的错误信息可直接展示给用户）。</summary>
    public static Result<UserConfig> Deserialize(string json)
    {
        var preview = Preview(json);
        if (!preview.IsValid || preview.Config is null)
        {
            return Result<UserConfig>.Fail(string.Join("；", preview.Errors));
        }

        return Result<UserConfig>.Ok(preview.Config);
    }

    /// <summary>
    /// 校验并生成导入预览（不落盘、不修改任何状态，供界面"逐项预览"消费）。
    /// </summary>
    public static ConfigImportPreview Preview(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Invalid(Array.Empty<string>(), "配置内容为空");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                // 备注与尾随逗号一律拒绝（避免不同编辑器产物差异被静默吞掉）
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        }
        catch (JsonException ex)
        {
            return Invalid(Array.Empty<string>(), $"配置不是合法的 JSON：{ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid(Array.Empty<string>(), "配置根节点必须是 JSON 对象");
            }

            var errors = new List<string>();
            var root = document.RootElement;

            // 1) schemaVersion
            var schemaVersion = ReadInt32(root, "schemaVersion", errors);
            if (schemaVersion is null)
            {
                if (errors.Count == 0)
                {
                    errors.Add("配置缺少 schemaVersion 字段（无法判断架构版本，已拒绝导入）");
                }

                return Invalid(errors, "schemaVersion 缺失或非法");
            }

            if (!UserConfigSchema.IsSupported(schemaVersion.Value))
            {
                return Invalid(
                    new[] { UserConfigSchema.DescribeUnsupported(schemaVersion.Value) },
                    "schemaVersion 不受支持");
            }

            // 2) product
            var product = ReadString(root, "product", errors);
            if (product is null)
            {
                if (errors.Count == 0)
                {
                    errors.Add("配置缺少 product 字段（无法判断归属产品，已拒绝导入）");
                }

                return Invalid(errors, "product 缺失或非法");
            }

            // 3) preferences
            var preferencesResult = ReadPreferences(root, errors);
            if (preferencesResult is null)
            {
                return Invalid(errors, "preferences 字段非法");
            }

            // 4) profiles
            var profilesResult = ReadProfiles(root, errors);
            if (profilesResult is null)
            {
                return Invalid(errors, "profiles 字段非法");
            }

            if (errors.Count > 0)
            {
                return Invalid(errors, "存在字段级错误");
            }

            var config = new UserConfig
            {
                SchemaVersion = schemaVersion.Value,
                Product = product,
                Preferences = preferencesResult,
                Profiles = profilesResult,
            };

            return new ConfigImportPreview
            {
                IsValid = true,
                SchemaVersion = schemaVersion.Value,
                Items = Array.Empty<ConfigImportItem>(),
                Errors = Array.Empty<string>(),
                Config = config,
            };
        }
    }

    /// <summary>
    /// 生成"当前配置 + 待导入 JSON"的逐项差异预览（schema 校验失败时直接返回无效预览）。
    /// </summary>
    public static ConfigImportPreview PreviewImport(UserConfig current, string json)
    {
        ArgumentNullException.ThrowIfNull(current);
        var preview = Preview(json);
        if (!preview.IsValid || preview.Config is null)
        {
            return preview;
        }

        var incoming = preview.Config;
        var items = new List<ConfigImportItem>(incoming.Profiles.Count);
        foreach (var profile in incoming.Profiles)
        {
            var existing = current.Profiles.FirstOrDefault(p =>
                p.Key.Matches(profile.GameId, profile.HardwareTier));
            if (existing is null)
            {
                items.Add(new ConfigImportItem
                {
                    Action = ConfigImportAction.Add,
                    Key = profile.Key,
                    Summary = $"新增 {profile.Describe()}",
                });
            }
            else if (ProfilesEqual(existing, profile))
            {
                items.Add(new ConfigImportItem
                {
                    Action = ConfigImportAction.Unchanged,
                    Key = profile.Key,
                    Summary = $"{profile.Describe()}（无变化）",
                });
            }
            else
            {
                items.Add(new ConfigImportItem
                {
                    Action = ConfigImportAction.Update,
                    Key = profile.Key,
                    Summary = $"{profile.Describe()}（原值：{existing.Describe()}）",
                });
            }
        }

        return preview with { Items = items };
    }

    /// <summary>
    /// Profile 结构化相等比较（record 的集合成员为引用相等，导入预览必须逐项深比较，
    /// 否则反序列化产生的新列表实例会被误判为"有变化"）。
    /// </summary>
    private static bool ProfilesEqual(OptimizationProfile left, OptimizationProfile right) =>
        string.Equals(left.GameId, right.GameId, StringComparison.OrdinalIgnoreCase)
        && left.HardwareTier == right.HardwareTier
        && left.Intensity == right.Intensity
        && left.EnabledCategories.SequenceEqual(right.EnabledCategories, StringComparer.Ordinal)
        && left.DisabledRuleIds.SequenceEqual(right.DisabledRuleIds, StringComparer.Ordinal)
        && left.ExtraRuleIds.SequenceEqual(right.ExtraRuleIds, StringComparer.Ordinal);

    /// <summary>
    /// 执行导入合并：校验通过后把 Add/Update 条目合入当前配置（Unchanged 跳过），
    /// 返回合并后的新配置（不修改传入的 current，函数式更新）。
    /// </summary>
    public static Result<UserConfig> Import(UserConfig current, string json)
    {
        ArgumentNullException.ThrowIfNull(current);
        var preview = PreviewImport(current, json);
        if (!preview.IsValid || preview.Config is null)
        {
            return Result<UserConfig>.Fail(string.Join("；", preview.Errors));
        }

        var incoming = preview.Config;
        var merged = current.MergeProfiles(incoming.Profiles);
        if (!string.Equals(merged.Product, incoming.Product, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(incoming.Product))
        {
            return Result<UserConfig>.Fail(
                $"配置归属产品不匹配：当前为 {merged.Product}，待导入为 {incoming.Product}（拒绝串产品导入）");
        }

        return Result<UserConfig>.Ok(merged);
    }

    private static ConfigImportPreview Invalid(IReadOnlyList<string> errors, string fallback) =>
        new()
        {
            IsValid = false,
            SchemaVersion = 0,
            Items = Array.Empty<ConfigImportItem>(),
            Errors = errors.Count > 0 ? errors : new[] { fallback },
            Config = null,
        };

    private static void WriteConfig(Utf8JsonWriter writer, UserConfig config)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", config.SchemaVersion);
        writer.WriteString("product", config.Product);

        writer.WriteStartArray("profiles");
        foreach (var profile in config.Profiles)
        {
            writer.WriteStartObject();
            writer.WriteString("gameId", profile.GameId);
            if (profile.HardwareTier is not null)
            {
                writer.WriteString("hardwareTier", profile.HardwareTier.Value.ToString());
            }

            writer.WriteString("intensity", profile.Intensity.ToString());
            WriteStringArray(writer, "enabledCategories", profile.EnabledCategories);
            WriteStringArray(writer, "disabledRuleIds", profile.DisabledRuleIds);
            WriteStringArray(writer, "extraRuleIds", profile.ExtraRuleIds);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartObject("preferences");
        writer.WriteBoolean("autoStartWithWindows", config.Preferences.AutoStartWithWindows);
        writer.WriteBoolean("startMinimizedToTray", config.Preferences.StartMinimizedToTray);
        writer.WriteBoolean("checkUpdatesOnStartup", config.Preferences.CheckUpdatesOnStartup);
        writer.WriteString("theme", config.Preferences.Theme);
        writer.WriteBoolean("confirmBeforeDangerRules", config.Preferences.ConfirmBeforeDangerRules);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static int? ReadInt32(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            errors.Add($"配置缺少 {name} 字段");
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            errors.Add($"配置字段 {name} 必须是整数（实际：{DescribeKind(element)}）");
            return null;
        }

        return value;
    }

    private static string? ReadString(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            errors.Add($"配置缺少 {name} 字段");
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"配置字段 {name} 必须是字符串（实际：{DescribeKind(element)}）");
            return null;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add($"配置字段 {name} 不能为空");
            return null;
        }

        return text.Trim();
    }

    private static UserPreferences? ReadPreferences(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("preferences", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return new UserPreferences();
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("配置字段 preferences 必须是对象");
            return null;
        }

        var defaults = new UserPreferences();
        var autoStart = ReadOptionalBool(element, "autoStartWithWindows", defaults.AutoStartWithWindows, errors);
        var minimized = ReadOptionalBool(element, "startMinimizedToTray", defaults.StartMinimizedToTray, errors);
        var updates = ReadOptionalBool(element, "checkUpdatesOnStartup", defaults.CheckUpdatesOnStartup, errors);
        var confirm = ReadOptionalBool(element, "confirmBeforeDangerRules", defaults.ConfirmBeforeDangerRules, errors);
        var theme = ReadTheme(element, defaults.Theme, errors);

        return new UserPreferences
        {
            AutoStartWithWindows = autoStart,
            StartMinimizedToTray = minimized,
            CheckUpdatesOnStartup = updates,
            ConfirmBeforeDangerRules = confirm,
            Theme = theme ?? defaults.Theme,
        };
    }

    private static IReadOnlyList<OptimizationProfile>? ReadProfiles(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("profiles", out var element)
            || element.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<OptimizationProfile>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add("配置字段 profiles 必须是数组");
            return null;
        }

        var profiles = new List<OptimizationProfile>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < element.GetArrayLength(); i++)
        {
            var item = element[i];
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"profiles[{i}] 必须是对象");
                return null;
            }

            var gameId = ReadString(item, "gameId", errors);
            if (gameId is null)
            {
                return null;
            }

            var errorCountBefore = errors.Count;
            var hardwareTier = ReadOptionalEnum<HardwareTier>(item, "hardwareTier", errors, i);
            if (errors.Count > errorCountBefore)
            {
                return null;
            }

            var intensity = ReadOptionalEnum<ProfileIntensity>(item, "intensity", errors, i) ?? ProfileIntensity.Balanced;
            if (errors.Count > errorCountBefore)
            {
                return null;
            }

            var enabledCategories = ReadOptionalStringArray(item, "enabledCategories", errors, i);
            var disabledRuleIds = ReadOptionalStringArray(item, "disabledRuleIds", errors, i);
            var extraRuleIds = ReadOptionalStringArray(item, "extraRuleIds", errors, i);
            if (errors.Count > errorCountBefore)
            {
                return null;
            }

            var key = $"{gameId.ToLowerInvariant()}|{hardwareTier?.ToString() ?? "*"}";
            if (!seenKeys.Add(key))
            {
                errors.Add($"profiles[{i}] 存储键重复：gameId={gameId}、hardwareTier={hardwareTier?.ToString() ?? "不限"}（同一组合只能有一个 Profile）");
                return null;
            }

            profiles.Add(new OptimizationProfile
            {
                GameId = gameId,
                HardwareTier = hardwareTier,
                Intensity = intensity,
                EnabledCategories = enabledCategories,
                DisabledRuleIds = disabledRuleIds,
                ExtraRuleIds = extraRuleIds,
            });
        }

        return profiles;
    }

    private static bool ReadOptionalBool(JsonElement parent, string name, bool defaultValue, List<string> errors)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
        {
            errors.Add($"配置字段 preferences.{name} 必须是布尔值（实际：{DescribeKind(element)}）");
            return defaultValue;
        }

        return element.GetBoolean();
    }

    /// <summary>读取 preferences.theme：缺失/null 走默认值；类型或取值非法时记错误并返回 null（拒绝导入）。</summary>
    private static string? ReadTheme(JsonElement preferences, string defaultValue, List<string> errors)
    {
        if (!preferences.TryGetProperty("theme", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"配置字段 preferences.theme 必须是字符串（实际：{DescribeKind(element)}）");
            return null;
        }

        var theme = element.GetString()?.Trim();
        if (string.IsNullOrEmpty(theme))
        {
            errors.Add("配置字段 preferences.theme 不能为空（应为 System|Light|Dark）");
            return null;
        }

        if (!AllowedThemes.Contains(theme))
        {
            errors.Add($"配置字段 preferences.theme 无效：{theme}（应为 System|Light|Dark）");
            return null;
        }

        return theme;
    }

    private static T? ReadOptionalEnum<T>(JsonElement parent, string name, List<string> errors, int profileIndex)
        where T : struct, Enum
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (Enum.TryParse<T>(text.Trim(), ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                errors.Add($"profiles[{profileIndex}].{name} 无效：{text}（应为 {string.Join('|', Enum.GetNames<T>())}）");
                return null;

            case JsonValueKind.Number when element.TryGetInt32(out var numeric):
                // 数字形式按枚举底层值解析（容错导入），非法值同样拒绝
                if (Enum.IsDefined(typeof(T), numeric))
                {
                    return (T)Enum.ToObject(typeof(T), numeric);
                }

                errors.Add($"profiles[{profileIndex}].{name} 无效：{numeric}（枚举中不存在该值）");
                return null;

            default:
                errors.Add($"profiles[{profileIndex}].{name} 必须是字符串或省略（实际：{DescribeKind(element)}）");
                return null;
        }
    }

    private static IReadOnlyList<string> ReadOptionalStringArray(
        JsonElement parent, string name, List<string> errors, int profileIndex)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return Array.Empty<string>();
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"profiles[{profileIndex}].{name} 必须是字符串数组");
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"profiles[{profileIndex}].{name} 的元素必须是字符串");
                return Array.Empty<string>();
            }

            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                errors.Add($"profiles[{profileIndex}].{name} 包含空字符串元素");
                return Array.Empty<string>();
            }

            values.Add(text.Trim());
        }

        return values;
    }

    private static string DescribeKind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "对象",
        JsonValueKind.Array => "数组",
        JsonValueKind.String => "字符串",
        JsonValueKind.Number => "数字",
        JsonValueKind.True => "布尔值 true",
        JsonValueKind.False => "布尔值 false",
        JsonValueKind.Null => "null",
        _ => element.ValueKind.ToString(),
    };
}
