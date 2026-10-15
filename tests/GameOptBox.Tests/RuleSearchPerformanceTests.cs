// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;
using PcOpt.Products.GameOptBox.ViewModels;
using Xunit.Abstractions;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>
/// 全局搜索性能验收（QA 独立测量，T30；PRD B-P0-10 "全局搜索 ≤ 200ms"）。
/// 使用<b>真实 68 条签名规则包</b>（经真实验签路径加载）测量，断言非空（非空断言），
/// 并对 ViewModel 的 LastSearchElapsedMs 指标路径做端到端校验。
/// </summary>
public sealed class RuleSearchPerformanceTests
{
    private const int BudgetMs = 200;

    private readonly ITestOutputHelper _output;

    /// <summary>初始化性能测试。</summary>
    public RuleSearchPerformanceTests(ITestOutputHelper output) => _output = output;

    private static (RuleRepository Repository, TweakEngine Engine) LoadRealPackage()
    {
        var verifier = RuleSignatureVerifier.CreateFromFile(RepoLayout.PublicKeyPath);
        verifier.IsSuccess.Should().BeTrue(verifier.Error);

        var temp = new TempDirectory(); // 生命周期随进程；仅测试用临时 Journal
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal")),
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance,
            verifier.Value);

        var repository = new RuleRepository(engine, RepoLayout.TweaksDirectory, RepoLayout.PublicKeyPath, NullLogger<RuleRepository>.Instance);
        var report = repository.LoadPackage();
        report.IsSuccess.Should().BeTrue(report.Error);
        report.Value!.Loaded.Should().BeGreaterThanOrEqualTo(60, "性能测量必须基于完整首发规则包");
        return (repository, engine);
    }

    [Fact]
    public void Search_OverRealRulePackage_IsWithinBudget_AndNonVacuous()
    {
        var (repository, _) = LoadRealPackage();

        // 非空断言：确保确实加载到了规则（避免空集合上的"假通过"）。
        repository.Count.Should().BeGreaterThanOrEqualTo(60);

        var sampleToken = repository.Rules.First().Id.Split('.').First();

        // 预热（消除 JIT / 首次分配的干扰）
        for (var i = 0; i < 50; i++)
        {
            _ = repository.Search(string.Empty, null, includeSensitive: true);
            _ = repository.Search(sampleToken, null, includeSensitive: true);
        }

        string[] queries = { string.Empty, sampleToken, "service", "游戏", "隐私", "nvidia", "不存在的关键词zzz" };
        foreach (var query in queries)
        {
            var matches = repository.Search(query, null, includeSensitive: true);
            if (query == sampleToken)
            {
                matches.Should().NotBeEmpty($"关键词“{query}”应命中至少一条真实规则（非空断言）");
            }

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 200; i++)
            {
                _ = repository.Search(query, null, includeSensitive: true);
            }

            stopwatch.Stop();
            var averageMs = stopwatch.Elapsed.TotalMilliseconds / 200;
            _output.WriteLine($"query=[{query}] matches={matches.Count} avg={averageMs:F4}ms over 200 iters");
            averageMs.Should().BeLessThan(BudgetMs,
                $"关键词“{query}”的平均搜索耗时应 ≤ {BudgetMs}ms（实测 {averageMs:F3}ms）");
        }
    }

    [Fact]
    public void Search_LargeSyntheticSet_StaysWithinBudget()
    {
        // 压力放大：以真实规则为模板放大到 5000 条，验证线性扫描在该量级仍满足预算（留足余量）。
        var (repository, _) = LoadRealPackage();
        var template = repository.Rules.First();
        var big = new List<TweakRule>(5000);
        for (var i = 0; i < 5000; i++)
        {
            big.Add(new TweakRule
            {
                Id = $"{template.Id}.{i}",
                Category = template.Category,
                Risk = template.Risk,
                DisplayName = template.DisplayName + i,
                Description = template.Description,
                Actions = template.Actions,
            });
        }

        for (var i = 0; i < 20; i++)
        {
            _ = RuleSearch.Filter(big, "5", null, includeSensitive: true);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = RuleSearch.Filter(big, "5", null, includeSensitive: true);
        stopwatch.Stop();

        result.Should().NotBeEmpty("放大集内应能命中关键词（非空断言）");
        _output.WriteLine($"synthetic n=5000 keyword=5 matches={result.Count} elapsed={stopwatch.Elapsed.TotalMilliseconds:F4}ms");
        stopwatch.Elapsed.TotalMilliseconds.Should().BeLessThan(BudgetMs,
            $"5000 条规则的关键词搜索应 ≤ {BudgetMs}ms（实测 {stopwatch.Elapsed.TotalMilliseconds:F3}ms）");
    }

    [Fact]
    public void RuleBrowserViewModel_ReportsElapsedWithinBudget()
    {
        var (repository, engine) = LoadRealPackage();
        var coordinator = new RuleActionCoordinator(repository, new FakeDialogService(), new RecordingNotifier());

        var viewModel = new RuleBrowserViewModel(repository, coordinator)
        {
            ShowSensitive = true,
        };

        var sampleToken = repository.Rules.First().Id.Split('.').First();
        viewModel.SearchText = sampleToken;

        viewModel.ResultCount.Should().BeGreaterThan(0, "搜索结果不应为空（非空断言）");
        viewModel.LastSearchElapsedMs.Should().BeGreaterThanOrEqualTo(0);
        _output.WriteLine($"viewmodel query=[{sampleToken}] resultCount={viewModel.ResultCount} elapsedMs={viewModel.LastSearchElapsedMs}");
        viewModel.LastSearchElapsedMs.Should().BeLessThan(BudgetMs,
            $"规则浏览器上报的搜索耗时应 ≤ {BudgetMs}ms（实测 {viewModel.LastSearchElapsedMs}ms）");
        viewModel.SearchSummary.Should().Contain("ms");

        // 敏感项默认折叠：全新实例默认不含危险项（红线 #6）。
        var fresh = new RuleBrowserViewModel(repository, coordinator);
        fresh.ShowSensitive.Should().BeFalse("危险项默认折叠（红线 #6）");
        fresh.Items.Should().NotContain(item => item.Risk == RiskLevel.Danger);
    }
}
