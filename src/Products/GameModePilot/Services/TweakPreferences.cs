// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Products.GameModePilot.Services;

/// <summary>
/// 逐项开关偏好（PRD A-P1-2：每个调整项可单独开关）。
/// 由"调整项网格"更新；<see cref="GuardianService"/> 构造会话请求时读取，关闭的项不再应用。
/// 线程安全；进程内单例。
/// </summary>
public sealed class TweakPreferences
{
    private readonly HashSet<string> _disabledRuleIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>指定规则是否启用（默认启用）。</summary>
    public bool IsEnabled(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return true;
        }

        lock (_gate)
        {
            return !_disabledRuleIds.Contains(ruleId);
        }
    }

    /// <summary>设置指定规则的启用状态。</summary>
    public void SetEnabled(string ruleId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return;
        }

        lock (_gate)
        {
            if (enabled)
            {
                _disabledRuleIds.Remove(ruleId);
            }
            else
            {
                _disabledRuleIds.Add(ruleId);
            }
        }
    }

    /// <summary>当前被关闭的规则 Id 集合（供会话请求使用）。</summary>
    public IReadOnlyList<string> DisabledRuleIds
    {
        get
        {
            lock (_gate)
            {
                return _disabledRuleIds.ToList();
            }
        }
    }
}
