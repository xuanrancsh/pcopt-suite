// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using PcOpt.Core.Tweak;
using YamlDotNet.Serialization;

namespace PcOpt.Core.Config;

/// <summary>
/// TweakRule YAML 序列化器（架构 3.5）。
/// 与 <see cref="Tweak.TweakRuleParser"/> 严格 round-trip：<c>Parse(Write(rule))</c> 语义等价于
/// <c>rule</c>（含全部动作类型、全部 OsCondition 字段、Binary Base64、MultiString 列表等边界）。
/// 输出采用 camelCase 键名（id / displayName / consequenceHints ...），解析端大小写不敏感，双向兼容。
/// 实现方式：先构造与解析端读取结构一致的 <see cref="Dictionary{TKey, TValue}"/> 再交给 YamlDotNet，
/// 不向模型类型引入序列化特性（模型与 YamlDotNet 解耦）。
/// </summary>
public static class TweakRuleWriter
{
    /// <summary>
    /// 将规则序列化为 YAML 文本。输出确定（键顺序固定），便于版本管理与 diff。
    /// </summary>
    /// <exception cref="ArgumentNullException">rule 为 null。</exception>
    public static string Write(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Serialize(ToMap(rule));
    }

    /// <summary>
    /// 将规则写入文件（UTF-8 无 BOM；先写临时文件再整体替换，避免半截文件被误加载）。
    /// </summary>
    public static Result WriteToFile(TweakRule rule, string path)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Fail("规则文件路径为空");
        }

        try
        {
            var yaml = Write(rule);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"规则文件写入失败：{ex.Message}");
        }
    }

    private static string Serialize(Dictionary<string, object> map)
    {
        var serializer = new SerializerBuilder().Build();
        return serializer.Serialize(map);
    }

    /// <summary>构造与 TweakRuleParser 读取结构一致的字典（键顺序 = 文档建议阅读顺序）。</summary>
    private static Dictionary<string, object> ToMap(TweakRule rule)
    {
        var map = new Dictionary<string, object>
        {
            ["id"] = rule.Id,
            ["category"] = rule.Category,
            ["risk"] = rule.Risk.ToString(),
            ["displayName"] = rule.DisplayName,
        };

        if (!string.IsNullOrEmpty(rule.Description))
        {
            map["description"] = rule.Description;
        }

        if (rule.ConsequenceHints.Length > 0)
        {
            map["consequenceHints"] = rule.ConsequenceHints.Cast<object>().ToArray();
        }

        if (rule.Conditions.Length > 0)
        {
            map["conditions"] = rule.Conditions.Select(ToMap).Cast<object>().ToArray();
        }

        map["actions"] = rule.Actions.Select(ToMap).Cast<object>().ToArray();
        return map;
    }

    private static Dictionary<string, object> ToMap(OsCondition condition)
    {
        var map = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(condition.OsName))
        {
            map["osName"] = condition.OsName;
        }

        if (condition.MinBuild is not null)
        {
            map["minBuild"] = condition.MinBuild.Value;
        }

        if (condition.MaxBuild is not null)
        {
            map["maxBuild"] = condition.MaxBuild.Value;
        }

        if (!string.IsNullOrEmpty(condition.CpuArchitecture))
        {
            map["cpuArchitecture"] = condition.CpuArchitecture;
        }

        if (!string.IsNullOrEmpty(condition.GpuVendor))
        {
            map["gpuVendor"] = condition.GpuVendor;
        }

        return map;
    }

    private static Dictionary<string, object> ToMap(TweakAction action) => action switch
    {
        RegistrySetAction registry => ToMap(registry),
        ServiceStartTypeAction service => new Dictionary<string, object>
        {
            ["type"] = "ServiceStartType",
            ["serviceName"] = service.ServiceName,
            ["target"] = service.Target.ToString(),
        },
        ScheduledTaskAction task => new Dictionary<string, object>
        {
            ["type"] = "ScheduledTask",
            ["taskPath"] = task.TaskPath,
            ["enable"] = task.Enable,
        },
        PowerCfgAction power => ToMap(power),
        ProcessPriorityAction priority => ToMap(priority),
        TimerResolutionAction timer => new Dictionary<string, object>
        {
            ["type"] = "TimerResolution",
            ["desiredResolution100Ns"] = timer.DesiredResolution100Ns.ToString(CultureInfo.InvariantCulture),
        },
        ShellAction shell => ToMap(shell),
        _ => throw new ArgumentOutOfRangeException(
            nameof(action), action.GetType().Name, "未知的动作类型，无法序列化（新增动作类型请在此扩展）"),
    };

    private static Dictionary<string, object> ToMap(RegistrySetAction action)
    {
        var map = new Dictionary<string, object>
        {
            ["type"] = "RegistrySet",
            ["path"] = action.Path,
            ["name"] = action.Name,
            ["valueKind"] = action.Kind.ToString(),
        };

        // value 的 YAML 表示与 ConvertRegistryValue 的反向读取严格对应：
        // Binary → Base64 字符串；MultiString → 字符串列表；其余 → 标量（数字/字符串）。
        switch (action.Kind)
        {
            case RegistryValueKind.Binary:
                map["value"] = Convert.ToBase64String((byte[])action.Value);
                break;
            case RegistryValueKind.MultiString:
                map["value"] = ((string[])action.Value).Cast<object>().ToArray();
                break;
            case RegistryValueKind.DWord:
                map["value"] = (int)action.Value;
                break;
            case RegistryValueKind.QWord:
                map["value"] = (long)action.Value;
                break;
            default:
                map["value"] = action.Value?.ToString() ?? string.Empty;
                break;
        }

        return map;
    }

    private static Dictionary<string, object> ToMap(PowerCfgAction action)
    {
        var map = new Dictionary<string, object>
        {
            ["type"] = "PowerCfg",
            ["kind"] = action.Kind.ToString(),
        };

        // List 操作允许无 argument；其余为空视为非法（解析端同样拒绝），此处原样写出。
        if (!string.IsNullOrEmpty(action.Argument))
        {
            map["argument"] = action.Argument;
        }

        return map;
    }

    private static Dictionary<string, object> ToMap(ProcessPriorityAction action)
    {
        var map = new Dictionary<string, object>
        {
            ["type"] = "ProcessPriority",
            ["processName"] = action.ProcessName,
            ["priority"] = action.Priority.ToString(),
        };

        if (action.AffinityMask is not null)
        {
            map["affinityMask"] = action.AffinityMask.Value.ToString(CultureInfo.InvariantCulture);
        }

        return map;
    }

    private static Dictionary<string, object> ToMap(ShellAction action)
    {
        var map = new Dictionary<string, object>
        {
            ["type"] = "Shell",
            ["command"] = action.Command,
        };

        if (!string.IsNullOrEmpty(action.Arguments))
        {
            map["arguments"] = action.Arguments;
        }

        return map;
    }
}
