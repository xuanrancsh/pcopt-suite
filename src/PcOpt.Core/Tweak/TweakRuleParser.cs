// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections;
using System.Globalization;
using Microsoft.Win32;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace PcOpt.Core.Tweak;

/// <summary>
/// YAML 规则包解析器（架构 3.1 / 3.5）。
/// 一个 YAML 文件对应一条 <see cref="TweakRule"/>；动作通过 <c>type</c> 字段区分
/// （RegistrySet / ServiceStartType / ScheduledTask / PowerCfg / ProcessPriority /
/// TimerResolution / Shell），新增动作类型时在此扩展 switch。
/// 解析过程不触碰系统、不执行任何动作；签名验证由 <see cref="RuleSignatureVerifier"/> 负责，
/// 二者在 <see cref="TweakEngine.LoadRulePackage"/> 中组合。
/// </summary>
public static class TweakRuleParser
{
    /// <summary>动作 type 字段名（兼容 "action" 别名）。</summary>
    private const string ActionTypeKey = "type";

    /// <summary>
    /// 解析单条规则 YAML。所有面向用户的错误信息使用明确中文（架构 8.2）。
    /// </summary>
    public static Result<TweakRule> Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return Result<TweakRule>.Fail("规则内容为空");
        }

        Dictionary<string, object> root;
        try
        {
            var deserializer = new DeserializerBuilder().Build();
            root = deserializer.Deserialize<Dictionary<string, object>>(new StringReader(yaml))
                ?? new Dictionary<string, object>();
        }
        catch (YamlException ex)
        {
            return Result<TweakRule>.Fail($"YAML 解析失败：{ex.Message}");
        }

        if (root.Count == 0)
        {
            return Result<TweakRule>.Fail("YAML 文档为空（或不是映射结构）");
        }

        var id = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result<TweakRule>.Fail("规则缺少必填字段 'id'");
        }

        var idValidation = ValidateRuleId(id);
        if (!idValidation.IsSuccess)
        {
            return Result<TweakRule>.Fail(idValidation.Error);
        }

        var category = GetString(root, "category");
        if (string.IsNullOrWhiteSpace(category))
        {
            return Result<TweakRule>.Fail($"规则“{id}”缺少必填字段 'category'（性能|游戏|延迟|网络|隐私|体验）");
        }

        var riskText = GetString(root, "risk");
        if (string.IsNullOrWhiteSpace(riskText))
        {
            return Result<TweakRule>.Fail($"规则“{id}”缺少必填字段 'risk'（Safe|Notice|Danger）");
        }

        if (!Enum.TryParse<RiskLevel>(riskText.Trim(), ignoreCase: true, out var risk))
        {
            return Result<TweakRule>.Fail($"规则“{id}”的 risk 值无效：{riskText}（应为 Safe|Notice|Danger）");
        }

        var displayName = GetString(root, "displayName");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result<TweakRule>.Fail($"规则“{id}”缺少必填字段 'displayName'（人话名）");
        }

        var hints = GetStringList(root, "consequenceHints");
        if (hints.Count > MaxConsequenceHints)
        {
            return Result<TweakRule>.Fail($"规则“{id}”的 consequenceHints 最多 {MaxConsequenceHints} 条（PRD 5.3 不恐吓原则）");
        }

        var conditionsResult = ParseConditions(root);
        if (!conditionsResult.IsSuccess)
        {
            return Result<TweakRule>.Fail(conditionsResult.Error);
        }

        var actionsResult = ParseActions(root);
        if (!actionsResult.IsSuccess)
        {
            return Result<TweakRule>.Fail(actionsResult.Error);
        }

        var rule = new TweakRule
        {
            Id = id.Trim(),
            Category = category.Trim(),
            Risk = risk,
            DisplayName = displayName.Trim(),
            Description = (GetString(root, "description") ?? string.Empty).Trim(),
            ConsequenceHints = hints.ToArray(),
            Conditions = conditionsResult.Value!,
            Actions = actionsResult.Value!,
        };
        return Result<TweakRule>.Ok(rule);
    }

    /// <summary>后果提示最大条数（PRD 5.3）。</summary>
    private const int MaxConsequenceHints = 3;

    private static Result ValidateRuleId(string id)
    {
        var trimmed = id.Trim();
        var segments = trimmed.Split('.');
        if (segments.Length < 3 || segments.Any(string.IsNullOrWhiteSpace))
        {
            return Result.Fail($"规则 Id 格式无效：{trimmed}（命名规范：分类.对象.动作，如 svc.sysmain.disable）");
        }

        return Result.Ok();
    }

    private static Result<OsCondition[]> ParseConditions(IReadOnlyDictionary<string, object> root)
    {
        var nodes = GetList(root, "conditions");
        if (nodes is null)
        {
            return Result<OsCondition[]>.Ok(Array.Empty<OsCondition>());
        }

        var conditions = new List<OsCondition>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var map = AsMap(nodes[i]);
            if (map is null)
            {
                return Result<OsCondition[]>.Fail($"conditions[{i}] 格式无效：应为映射");
            }

            var minBuild = ParseOptionalInt32(map, "minBuild", out var minBuildError);
            if (minBuildError is not null)
            {
                return Result<OsCondition[]>.Fail($"conditions[{i}].minBuild 无效：{minBuildError}");
            }

            var maxBuild = ParseOptionalInt32(map, "maxBuild", out var maxBuildError);
            if (maxBuildError is not null)
            {
                return Result<OsCondition[]>.Fail($"conditions[{i}].maxBuild 无效：{maxBuildError}");
            }

            conditions.Add(new OsCondition
            {
                OsName = GetString(map, "osName"),
                MinBuild = minBuild,
                MaxBuild = maxBuild,
                CpuArchitecture = GetString(map, "cpuArchitecture"),
                GpuVendor = GetString(map, "gpuVendor"),
            });
        }

        return Result<OsCondition[]>.Ok(conditions.ToArray());
    }

    private static Result<TweakAction[]> ParseActions(IReadOnlyDictionary<string, object> root)
    {
        var nodes = GetList(root, "actions");
        if (nodes is null || nodes.Count == 0)
        {
            return Result<TweakAction[]>.Fail("规则必须包含至少一个动作（actions 列表）");
        }

        var actions = new List<TweakAction>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var action = ParseAction(nodes[i], i);
            if (!action.IsSuccess)
            {
                return Result<TweakAction[]>.Fail(action.Error);
            }

            actions.Add(action.Value!);
        }

        return Result<TweakAction[]>.Ok(actions.ToArray());
    }

    private static Result<TweakAction> ParseAction(object? node, int index)
    {
        var map = AsMap(node);
        if (map is null)
        {
            return Result<TweakAction>.Fail($"动作 {index} 格式无效：应为映射（含 type 字段）");
        }

        var type = (GetString(map, ActionTypeKey) ?? GetString(map, "action"))?.Trim();
        if (string.IsNullOrWhiteSpace(type))
        {
            return Result<TweakAction>.Fail($"动作 {index} 缺少 type 字段（RegistrySet|ServiceStartType|ScheduledTask|PowerCfg|ProcessPriority|TimerResolution|Shell）");
        }

        switch (type)
        {
            case "RegistrySet":
                return ParseRegistrySetAction(map, index);
            case "ServiceStartType":
                return ParseServiceStartTypeAction(map, index);
            case "ScheduledTask":
                return ParseScheduledTaskAction(map, index);
            case "PowerCfg":
                return ParsePowerCfgAction(map, index);
            case "ProcessPriority":
                return ParseProcessPriorityAction(map, index);
            case "TimerResolution":
                return ParseTimerResolutionAction(map, index);
            case "Shell":
                return ParseShellAction(map, index);
            default:
                return Result<TweakAction>.Fail($"动作 {index} 类型未知：{type}");
        }
    }

    private static Result<TweakAction> ParseRegistrySetAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var path = GetString(map, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<TweakAction>.Fail($"动作 {index}（RegistrySet）缺少 path");
        }

        var name = GetString(map, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<TweakAction>.Fail($"动作 {index}（RegistrySet）缺少 name");
        }

        var kindText = (GetString(map, "valueKind") ?? "String").Trim();
        if (!Enum.TryParse<RegistryValueKind>(kindText, ignoreCase: true, out var kind))
        {
            return Result<TweakAction>.Fail($"动作 {index}（RegistrySet）valueKind 无效：{kindText}（String|ExpandString|DWord|QWord|Binary|MultiString）");
        }

        var valueResult = ConvertRegistryValue(GetValue(map, "value"), kind);
        if (!valueResult.IsSuccess)
        {
            return Result<TweakAction>.Fail($"动作 {index}（RegistrySet）{valueResult.Error}");
        }

        return Result<TweakAction>.Ok(new RegistrySetAction(path.Trim(), name.Trim(), valueResult.Value!, kind));
    }

    private static Result<TweakAction> ParseServiceStartTypeAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var serviceName = GetString(map, "serviceName");
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ServiceStartType）缺少 serviceName");
        }

        var targetText = GetString(map, "target");
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ServiceStartType）缺少 target（Boot|System|Automatic|Manual|Disabled）");
        }

        if (!Enum.TryParse<ServiceStartMode>(targetText.Trim(), ignoreCase: true, out var target))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ServiceStartType）target 无效：{targetText}（Boot|System|Automatic|Manual|Disabled）");
        }

        return Result<TweakAction>.Ok(new ServiceStartTypeAction(serviceName.Trim(), target));
    }

    private static Result<TweakAction> ParseScheduledTaskAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var taskPath = GetString(map, "taskPath");
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ScheduledTask）缺少 taskPath");
        }

        var enableText = GetString(map, "enable");
        if (string.IsNullOrWhiteSpace(enableText) || !bool.TryParse(enableText.Trim(), out var enable))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ScheduledTask）enable 无效：{enableText ?? "（缺失）"}（应为 true|false）");
        }

        return Result<TweakAction>.Ok(new ScheduledTaskAction(taskPath.Trim(), enable));
    }

    private static Result<TweakAction> ParsePowerCfgAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var kindText = GetString(map, "kind");
        if (string.IsNullOrWhiteSpace(kindText))
        {
            return Result<TweakAction>.Fail($"动作 {index}（PowerCfg）缺少 kind（List|Import|SetActive|Delete）");
        }

        if (!Enum.TryParse<PowerActionKind>(kindText.Trim(), ignoreCase: true, out var kind))
        {
            return Result<TweakAction>.Fail($"动作 {index}（PowerCfg）kind 无效：{kindText}（List|Import|SetActive|Delete）");
        }

        var argument = GetString(map, "argument") ?? string.Empty;
        if (kind != PowerActionKind.List && string.IsNullOrWhiteSpace(argument))
        {
            return Result<TweakAction>.Fail($"动作 {index}（PowerCfg）{kind} 操作缺少 argument（电源方案 GUID 或 .pow 文件路径）");
        }

        return Result<TweakAction>.Ok(new PowerCfgAction(kind, argument.Trim()));
    }

    private static Result<TweakAction> ParseProcessPriorityAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var processName = GetString(map, "processName");
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ProcessPriority）缺少 processName");
        }

        var priorityText = GetString(map, "priority");
        if (string.IsNullOrWhiteSpace(priorityText))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ProcessPriority）缺少 priority（Idle|BelowNormal|Normal|AboveNormal|High|Realtime）");
        }

        if (!Enum.TryParse<ProcessPriorityClass>(priorityText.Trim(), ignoreCase: true, out var priority))
        {
            return Result<TweakAction>.Fail($"动作 {index}（ProcessPriority）priority 无效：{priorityText}（Idle|BelowNormal|Normal|AboveNormal|High|Realtime）");
        }

        var affinityText = GetString(map, "affinityMask");
        if (string.IsNullOrWhiteSpace(affinityText))
        {
            return Result<TweakAction>.Ok(new ProcessPriorityAction(processName.Trim(), priority, null));
        }

        var maskText = affinityText.Trim();
        try
        {
            var mask = maskText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(maskText[2..], 16)
                : long.Parse(maskText, NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (mask <= 0)
            {
                return Result<TweakAction>.Fail($"动作 {index}（ProcessPriority）affinityMask 无效：{maskText}（应为正数掩码）");
            }

            return Result<TweakAction>.Ok(new ProcessPriorityAction(processName.Trim(), priority, mask));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return Result<TweakAction>.Fail($"动作 {index}（ProcessPriority）affinityMask 无效：{maskText}（支持十进制或 0x 十六进制）");
        }
    }

    private static Result<TweakAction> ParseTimerResolutionAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var resolutionText = GetString(map, "desiredResolution100Ns");
        if (string.IsNullOrWhiteSpace(resolutionText)
            || !uint.TryParse(resolutionText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resolution))
        {
            return Result<TweakAction>.Fail($"动作 {index}（TimerResolution）desiredResolution100Ns 无效：{resolutionText ?? "（缺失）"}（100ns 单位，0.5ms = 5000）");
        }

        return Result<TweakAction>.Ok(new TimerResolutionAction(resolution));
    }

    private static Result<TweakAction> ParseShellAction(IReadOnlyDictionary<string, object> map, int index)
    {
        var command = GetString(map, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return Result<TweakAction>.Fail($"动作 {index}（Shell）缺少 command");
        }

        return Result<TweakAction>.Ok(new ShellAction(command.Trim(), GetString(map, "arguments") ?? string.Empty));
    }

    private static Result<object> ConvertRegistryValue(object? valueNode, RegistryValueKind kind)
    {
        switch (kind)
        {
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString:
                if (valueNode is null)
                {
                    return Result<object>.Fail("缺少 value 字段");
                }

                return Result<object>.Ok(valueNode.ToString() ?? string.Empty);

            case RegistryValueKind.DWord:
                if (!TryParseInt64(valueNode, out var dword) || dword is < int.MinValue or > int.MaxValue)
                {
                    return Result<object>.Fail($"DWord value 无效：{valueNode}（应为 32 位整数）");
                }

                return Result<object>.Ok((int)dword);

            case RegistryValueKind.QWord:
                if (!TryParseInt64(valueNode, out var qword))
                {
                    return Result<object>.Fail($"QWord value 无效：{valueNode}（应为 64 位整数）");
                }

                return Result<object>.Ok(qword);

            case RegistryValueKind.Binary:
            {
                if (valueNode is not string base64 || string.IsNullOrWhiteSpace(base64))
                {
                    return Result<object>.Fail("Binary value 无效：应为 Base64 字符串");
                }

                try
                {
                    return Result<object>.Ok(Convert.FromBase64String(base64.Trim()));
                }
                catch (FormatException)
                {
                    return Result<object>.Fail("Binary value 无效：不是合法的 Base64 字符串");
                }
            }

            case RegistryValueKind.MultiString:
            {
                if (valueNode is List<object> list)
                {
                    return Result<object>.Ok(list.Select(item => item?.ToString() ?? string.Empty).ToArray());
                }

                if (valueNode is string single)
                {
                    return Result<object>.Ok(new[] { single });
                }

                return Result<object>.Fail("MultiString value 无效：应为字符串列表");
            }

            default:
                return Result<object>.Fail($"不支持的 valueKind：{kind}");
        }
    }

    private static bool TryParseInt64(object? valueNode, out long value)
    {
        switch (valueNode)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static int? ParseOptionalInt32(IReadOnlyDictionary<string, object> map, string key, out string? error)
    {
        error = null;
        var text = GetString(map, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = text;
            return null;
        }

        return parsed;
    }

    /// <summary>
    /// 将 YAML 反序列化节点归一化为字符串键字典。
    /// YamlDotNet 非强类型反序列化对嵌套映射实际返回 <c>Dictionary&lt;object, object&gt;</c>
    /// （键运行时为 string），顶层映射才是 <c>Dictionary&lt;string, object&gt;</c>（TempProbe 实测），此处统一兼容。
    /// </summary>
    private static IReadOnlyDictionary<string, object>? AsMap(object? node)
    {
        switch (node)
        {
            case Dictionary<string, object> typed:
                return typed;
            case System.Collections.IDictionary dictionary:
                var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in dictionary)
                {
                    map[entry.Key?.ToString() ?? string.Empty] = entry.Value ?? string.Empty;
                }

                return map;
            default:
                return null;
        }
    }

    private static object? GetValue(IReadOnlyDictionary<string, object> map, string key)
    {
        if (map.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var pair in map)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? GetString(IReadOnlyDictionary<string, object> map, string key) =>
        GetValue(map, key)?.ToString();

    private static IReadOnlyList<object>? GetList(IReadOnlyDictionary<string, object> map, string key) =>
        GetValue(map, key) as List<object>;

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<string, object> map, string key)
    {
        var value = GetValue(map, key);
        if (value is List<object> list)
        {
            return list.Select(item => item?.ToString() ?? string.Empty).ToArray();
        }

        return Array.Empty<string>();
    }
}
