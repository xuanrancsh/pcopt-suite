// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>健康分输入信号（首页"电脑健康分"，PRD B-P0-10）。</summary>
/// <param name="StartupItemCount">开机自启项数量。</param>
/// <param name="NonEssentialServiceCount">可关闭的后台非必要服务数量。</param>
/// <param name="AppliedGameTweakCount">已应用的游戏优化项数量。</param>
/// <param name="TotalGameTweakCount">游戏优化项总数（分母）。</param>
public sealed record HealthSignals(
    int StartupItemCount,
    int NonEssentialServiceCount,
    int AppliedGameTweakCount,
    int TotalGameTweakCount);

/// <summary>健康分报告（各分项 0~100）。</summary>
/// <param name="Overall">总分（0~100，加权）。</param>
/// <param name="StartupScore">开机速度分项。</param>
/// <param name="BackgroundServiceScore">后台服务分项。</param>
/// <param name="ReadinessScore">游戏就绪分项。</param>
/// <param name="Grade">评级（优秀 / 良好 / 一般 / 待优化）。</param>
/// <param name="Suggestions">改进建议（人话，最多 3 条）。</param>
public sealed record HealthReport(
    int Overall,
    int StartupScore,
    int BackgroundServiceScore,
    int ReadinessScore,
    string Grade,
    IReadOnlyList<string> Suggestions);

/// <summary>
/// 首页健康分算法（T20，PRD B-P0-10："健康分算法文档化"）。
/// 权重（文档化，UI 与文档一致）：
/// <list type="bullet">
/// <item>开机速度（自启项）：权重 0.3；</item>
/// <item>后台服务（可关闭的非必要服务）：权重 0.3；</item>
/// <item>游戏就绪（已应用的游戏优化占比）：权重 0.4。</item>
/// </list>
/// 分项公式：
/// <list type="bullet">
/// <item>开机速度 = clamp(100 − max(0, 自启项数 − 3) × 5, 30, 100)（前 3 项视为正常）；</item>
/// <item>后台服务 = clamp(100 − 非必要服务数 × 2, 30, 100)；</item>
/// <item>游戏就绪 = clamp(round(已应用 ÷ 总数 × 100), 0, 100)（总数为 0 时记 0）。</item>
/// </list>
/// 全部为确定性纯计算，便于单元测试。
/// </summary>
public sealed class HealthScoreService
{
    /// <summary>开机速度分项权重。</summary>
    public const double StartupWeight = 0.3;

    /// <summary>后台服务分项权重。</summary>
    public const double BackgroundServiceWeight = 0.3;

    /// <summary>游戏就绪分项权重。</summary>
    public const double ReadinessWeight = 0.4;

    /// <summary>计算健康分报告。</summary>
    public HealthReport Compute(HealthSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var startup = Clamp(100 - (Math.Max(0, signals.StartupItemCount - 3) * 5), 30, 100);
        var background = Clamp(100 - (Math.Max(0, signals.NonEssentialServiceCount) * 2), 30, 100);
        var readiness = signals.TotalGameTweakCount > 0
            ? Clamp((int)Math.Round(signals.AppliedGameTweakCount * 100.0 / signals.TotalGameTweakCount), 0, 100)
            : 0;

        var overall = Clamp(
            (int)Math.Round((startup * StartupWeight) + (background * BackgroundServiceWeight) + (readiness * ReadinessWeight)),
            0,
            100);

        return new HealthReport(overall, startup, background, readiness, Grade(overall), BuildSuggestions(startup, background, readiness));
    }

    /// <summary>总分评级（PRD 3.6 线框：72/100 = 良好）。</summary>
    public static string Grade(int overall) => overall switch
    {
        >= 85 => "优秀",
        >= 70 => "良好",
        >= 50 => "一般",
        _ => "待优化",
    };

    private static IReadOnlyList<string> BuildSuggestions(int startup, int background, int readiness)
    {
        var suggestions = new List<string>();
        if (startup < 70)
        {
            suggestions.Add("关闭用不到的开机自启项，能明显加快开机速度");
        }

        if (background < 70)
        {
            suggestions.Add("把用不到的后台服务改为手动启动，游戏时更少抢资源");
        }

        if (readiness < 70)
        {
            suggestions.Add("到“游戏优化”里应用几项常用调整，让游戏跑得更稳");
        }

        return suggestions.Count == 0
            ? new[] { "电脑状态不错，保持即可" }
            : suggestions.Take(3).ToList();
    }

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
}
