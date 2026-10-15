// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>还原中心测试（T29，PRD B-P0-9）：单条 / 全部还原、会话列表、还原点、日志导出。</summary>
public sealed class RestoreCenterServiceTests
{
    private static TweakRule DemoRule() => new()
    {
        Id = "test.demo.rule",
        Category = "性能",
        Risk = RiskLevel.Safe,
        DisplayName = "测试规则",
        Description = "测试",
        Actions = new TweakAction[] { new FakeAction("a") },
    };

    private static (TweakEngine Engine, RuleRepository Repository, JournalStore Store) Build(TempDirectory temp)
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal"));
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new FakeActionHandler() },
            store,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance);
        var repository = new RuleRepository(engine, string.Empty, null, NullLogger<RuleRepository>.Instance);
        return (engine, repository, store);
    }

    private static RestoreCenterService BuildService(
        TweakEngine engine,
        RuleRepository repository,
        JournalStore store,
        FakeRestorePointService restorePoint) =>
        new(engine, repository, store, restorePoint, NullLogger<RestoreCenterService>.Instance);

    [Fact]
    public void ListAppliedRules_AfterApply_ContainsRule()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        engine.RegisterRule(DemoRule()).IsSuccess.Should().BeTrue();
        engine.ApplyRule("test.demo.rule", RuleRepository.ProductName).IsSuccess.Should().BeTrue();

        var service = BuildService(engine, repository, store, new FakeRestorePointService());
        var applied = service.ListAppliedRules();

        applied.Should().ContainSingle().Which.RuleId.Should().Be("test.demo.rule");
    }

    [Fact]
    public void RevertRule_RestoresAndClears()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        engine.RegisterRule(DemoRule()).IsSuccess.Should().BeTrue();
        engine.ApplyRule("test.demo.rule", RuleRepository.ProductName).IsSuccess.Should().BeTrue();

        var service = BuildService(engine, repository, store, new FakeRestorePointService());
        var revert = service.RevertRule("test.demo.rule");

        revert.IsSuccess.Should().BeTrue(revert.Error);
        revert.Value!.EntriesReverted.Should().Be(1);
        service.ListAppliedRules().Should().BeEmpty();
    }

    [Fact]
    public void RevertAll_RevertsEverything()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        engine.RegisterRule(DemoRule()).IsSuccess.Should().BeTrue();
        engine.ApplyRule("test.demo.rule", RuleRepository.ProductName).IsSuccess.Should().BeTrue();

        var service = BuildService(engine, repository, store, new FakeRestorePointService());
        var result = service.RevertAll();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.RevertedRules.Should().Be(1);
        result.Value.RevertedEntries.Should().Be(1);
        result.Value.FullyReverted.Should().BeTrue();
    }

    [Fact]
    public void ListSessions_ReflectsJournal()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        engine.RegisterRule(DemoRule()).IsSuccess.Should().BeTrue();
        engine.ApplyRule("test.demo.rule", RuleRepository.ProductName).IsSuccess.Should().BeTrue();

        var service = BuildService(engine, repository, store, new FakeRestorePointService());
        var sessions = service.ListSessions();

        sessions.Should().NotBeEmpty();
        sessions[0].Product.Should().Be(RuleRepository.ProductName);
        sessions[0].TotalEntries.Should().Be(1);
        sessions[0].OpenEntries.Should().Be(1);
    }

    [Fact]
    public void CreateRestorePoint_Delegates()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        var restorePoint = new FakeRestorePointService();
        var service = BuildService(engine, repository, store, restorePoint);

        var result = service.CreateRestorePoint("测试还原点");

        result.IsSuccess.Should().BeTrue(result.Error);
        restorePoint.Calls.Should().Be(1);
    }

    [Fact]
    public void ExportLog_WritesFile()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        engine.RegisterRule(DemoRule()).IsSuccess.Should().BeTrue();
        engine.ApplyRule("test.demo.rule", RuleRepository.ProductName).IsSuccess.Should().BeTrue();

        var service = BuildService(engine, repository, store, new FakeRestorePointService());
        var target = temp.Combine("logs", "operation-log.json");
        var result = service.ExportLog(target);

        result.IsSuccess.Should().BeTrue(result.Error);
        File.Exists(target).Should().BeTrue();
        File.ReadAllText(target).Should().Contain("test.demo.rule");
    }

    [Fact]
    public void ExportLog_EmptyPath_Fails()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        var service = BuildService(engine, repository, store, new FakeRestorePointService());
        service.ExportLog(string.Empty).IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// 缺陷 E 回归证明：还原日志瞬时读不到（排他句柄 / 杀软扫描）时，
    /// 还原必须<b>显式失败</b>，绝不能报告"已全部还原（0 条）"。
    /// </summary>
    [Fact]
    public void RevertRule_WhenJournalUnreadable_ShouldFail_NotReportSuccess()
    {
        using var temp = new TempDirectory();
        var (engine, repository, store) = Build(temp);
        engine.RegisterRule(DemoRule()).IsSuccess.Should().BeTrue();
        var apply = engine.ApplyRule("test.demo.rule", RuleRepository.ProductName);
        apply.IsSuccess.Should().BeTrue(apply.Error);
        var sessionId = apply.Value!.SessionId!;

        var service = BuildService(engine, repository, store, new FakeRestorePointService());

        // 独占占用还原日志文件：模拟排他句柄 / 杀软实时扫描导致的持续读失败
        using (new FileStream(store.GetSessionPath(sessionId), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var revert = service.RevertRule("test.demo.rule");

            revert.IsSuccess.Should().BeFalse("读不到还原日志时绝不能误报已还原");
            revert.Error.Should().Contain("还原日志");
        }

        // 释放句柄后可正常还原（证明仅瞬时不可读，而非常态）
        var afterRelease = service.RevertRule("test.demo.rule");
        afterRelease.IsSuccess.Should().BeTrue(afterRelease.Error);
        afterRelease.Value!.EntriesReverted.Should().Be(1);
    }
}
