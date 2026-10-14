// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Platform;

namespace PcOpt.Core.Tweak;

/// <summary>
/// <see cref="OsCondition"/> 匹配器（架构 3.1 条件执行）。
/// 语义：条件列表为空 = 匹配；多条条件之间 AND（全部满足才应用，保守策略）。
/// GPU 厂商在检测未接入（T6 前）时若条件指定了具体厂商，则**不匹配**（宁可不用，不可误用）。
/// </summary>
public static class OsConditionMatcher
{
    /// <summary>全部条件均满足才返回 true。</summary>
    public static bool Matches(IReadOnlyList<OsCondition>? conditions, ISystemEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (conditions is null || conditions.Count == 0)
        {
            return true;
        }

        return conditions.All(condition => MatchesOne(condition, environment));
    }

    /// <summary>单条条件匹配。</summary>
    public static bool MatchesOne(OsCondition condition, ISystemEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(condition.OsName)
            && !string.Equals(condition.OsName.Trim(), environment.OsVersion.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var build = environment.OsVersion.Build;
        if (condition.MinBuild is int minBuild && build < minBuild)
        {
            return false;
        }

        if (condition.MaxBuild is int maxBuild && build > maxBuild)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(condition.CpuArchitecture))
        {
            var requiredArchitecture = ParseArchitecture(condition.CpuArchitecture);
            if (requiredArchitecture is null || requiredArchitecture != environment.CpuArchitecture)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(condition.GpuVendor))
        {
            var requiredVendor = condition.GpuVendor.Trim();
            var isWildcard = string.Equals(requiredVendor, "any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requiredVendor, "unknown", StringComparison.OrdinalIgnoreCase);
            if (!isWildcard)
            {
                // GPU 厂商未知（检测未接入或识别失败）时保守不匹配
                if (string.IsNullOrWhiteSpace(environment.GpuVendor)
                    || !string.Equals(requiredVendor, environment.GpuVendor.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static CpuArchitecture? ParseArchitecture(string text) =>
        Enum.TryParse<CpuArchitecture>(text.Trim(), ignoreCase: true, out var parsed) ? parsed : null;
}
