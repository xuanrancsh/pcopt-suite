// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 规则搜索 / 过滤（T21，PRD B-P0-10：全局搜索 ≤ 200ms）。
/// 纯函数实现，不触碰系统，便于单元测试；对 ~百条量级的规则集为毫秒级线性扫描。
/// </summary>
public static class RuleSearch
{
    /// <summary>危险级（敏感）规则：默认折叠 + 默认关闭（PRD 6 红线 #6 / B 待确认 #3）。</summary>
    public static bool IsSensitive(TweakRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.Risk == RiskLevel.Danger;
    }

    /// <summary>单条规则是否命中关键词（在 Id / 人话名 / 说明 / 分类上做不区分大小写子串匹配）。</summary>
    public static bool Matches(TweakRule rule, string? query)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var keyword = query.Trim();
        return Contains(rule.Id, keyword)
            || Contains(rule.DisplayName, keyword)
            || Contains(rule.Description, keyword)
            || Contains(rule.Category, keyword);
    }

    /// <summary>
    /// 过滤规则集：命中关键词 + 分类匹配 + 敏感项可见性。
    /// </summary>
    /// <param name="rules">候选规则。</param>
    /// <param name="query">搜索关键词（空 = 不过滤）。</param>
    /// <param name="category">分类（空 = 全部分类）。</param>
    /// <param name="includeSensitive">是否包含危险（敏感）项。</param>
    public static IReadOnlyList<TweakRule> Filter(
        IEnumerable<TweakRule> rules,
        string? query,
        string? category,
        bool includeSensitive)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var result = new List<TweakRule>();
        foreach (var rule in rules)
        {
            if (!includeSensitive && IsSensitive(rule))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(category)
                && !string.Equals(rule.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Matches(rule, query))
            {
                result.Add(rule);
            }
        }

        return result;
    }

    /// <summary>可搜索分类的去重有序列表（用于分类下拉）。</summary>
    public static IReadOnlyList<string> DistinctCategories(IEnumerable<TweakRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .Select(rule => rule.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCulture)
            .ToList();
    }

    private static bool Contains(string? source, string keyword) =>
        !string.IsNullOrEmpty(source) && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
