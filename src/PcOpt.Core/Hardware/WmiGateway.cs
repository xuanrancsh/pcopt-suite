// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Management;
using System.Runtime.InteropServices;

namespace PcOpt.Core.Hardware;

/// <summary>
/// WMI 查询网关（T6 架构 3.3）。
/// 集中 WMI 访问：查询失败 / 命名空间不存在 / 无实例一律返回空列表而不抛异常
/// （WMI 在部分精简系统 / 虚拟机上不可用，必须优雅降级）。
/// 合规声明：仅只读查询（SELECT），不执行任何 WMI 写操作。
/// </summary>
internal sealed class WmiGateway
{
    /// <summary>执行 WQL 查询并返回行集合（每行为 属性名 → 值 字典；DBNull 转为 null）。</summary>
    public static IReadOnlyList<Dictionary<string, object?>> Query(string scope, string queryString)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, queryString);
            using var collection = searcher.Get();
            foreach (var instance in collection)
            {
                using (instance)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in instance.Properties)
                    {
                        var value = property.Value;
                        row[property.Name] = value is DBNull ? null : value;
                    }

                    rows.Add(row);
                }
            }
        }
        catch (ManagementException)
        {
            // WMI 类不存在 / 查询被拒绝：按"无数据"处理，由调用方降级
            return Array.Empty<Dictionary<string, object?>>();
        }
        catch (COMException)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        return rows;
    }

    /// <summary>读取行中的整型属性；缺失 / 类型不符返回 fallback。</summary>
    public static int GetInt32(IReadOnlyDictionary<string, object?> row, string name, int fallback = 0)
    {
        var value = GetValue(row, name);
        return value switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            long longValue => (int)Math.Clamp(longValue, int.MinValue, int.MaxValue),
            uint uintValue => (int)Math.Min(uintValue, int.MaxValue),
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => fallback,
        };
    }

    /// <summary>读取行中的 64 位整型属性；缺失 / 类型不符返回 fallback。</summary>
    public static long GetInt64(IReadOnlyDictionary<string, object?> row, string name, long fallback = 0)
    {
        var value = GetValue(row, name);
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            uint uintValue => uintValue,
            ulong ulongValue => unchecked((long)ulongValue),
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => fallback,
        };
    }

    /// <summary>读取行中的字符串属性；缺失返回 null。</summary>
    public static string? GetString(IReadOnlyDictionary<string, object?> row, string name) =>
        GetValue(row, name)?.ToString();

    private static object? GetValue(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) ? value : null;
}
