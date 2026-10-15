// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Tests.TestDoubles;

namespace PcOpt.Core.Tests.Journal;

/// <summary>
/// <see cref="RecoveryService"/> 单元测试：开机扫描 Open 会话、LIFO 还原、失败保持 Open（架构 3.2 / PRD A-P0-5）。
/// </summary>
public sealed class RecoveryServiceTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly JournalStore _store;
    private readonly FakeActionRestorer _restorer;
    private readonly RecoveryService _service;

    public RecoveryServiceTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "pcopt-recovery-tests", Guid.NewGuid().ToString("N"));
        _store = new JournalStore(NullLogger<JournalStore>.Instance, _rootDirectory);
        _restorer = new FakeActionRestorer();
        _service = new RecoveryService(_store, _restorer, NullLogger<RecoveryService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RecoverOpenSessions_ShouldRestoreInLifoOrder_AndMarkRecovered()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        var baseTime = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        _store.AppendEntries(sessionId, new[]
        {
            new JournalEntry("svc.sysmain.disable", 0, "{}", baseTime),
            new JournalEntry("svc.sysmain.disable", 1, "{}", baseTime.AddSeconds(1)),
            new JournalEntry("pw.setactive.plan", 0, "{}", baseTime.AddSeconds(2)),
        });

        var report = _service.RecoverOpenSessions();

        report.IsSuccess.Should().BeTrue();
        var value = report.Value!;
        value.SessionsRecovered.Should().Be(1);
        value.EntriesRestored.Should().Be(3);
        value.IsFullyRecovered.Should().BeTrue();

        // LIFO：最后应用的条目最先还原
        _restorer.RestoredEntries.Select(e => (e.RuleId, e.ActionIndex)).Should().Equal(
            ("pw.setactive.plan", 0),
            ("svc.sysmain.disable", 1),
            ("svc.sysmain.disable", 0));

        var session = _store.Load(sessionId).Value!;
        session.Status.Should().Be(JournalStatus.Recovered);
        session.Entries.Should().OnlyContain(e => e.RevertedAt != null);
    }

    [Fact]
    public void RecoverOpenSessions_ShouldKeepSessionOpen_WhenEntryRestoreFails()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        var baseTime = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        _store.AppendEntries(sessionId, new[]
        {
            new JournalEntry("svc.sysmain.disable", 0, "{}", baseTime),
            new JournalEntry("reg.test.value", 1, "{}", baseTime.AddSeconds(1)),
        });

        _restorer.RestoreHandler = entry =>
            entry.RuleId == "reg.test.value"
                ? Result.Fail("模拟还原失败：需要管理员权限")
                : Result.Ok();

        var report = _service.RecoverOpenSessions();

        report.IsSuccess.Should().BeTrue();
        var value = report.Value!;
        value.EntriesRestored.Should().Be(1);
        value.FailedRuleIds.Should().Contain("reg.test.value");
        value.RemainingOpenSessionIds.Should().Contain(sessionId);
        value.IsFullyRecovered.Should().BeFalse();

        // 失败的会话保持 Open：下次启动继续尝试（系统最终可恢复）
        var session = _store.Load(sessionId).Value!;
        session.Status.Should().Be(JournalStatus.Open);
        session.Entries.Count(e => e.RevertedAt is not null).Should().Be(1);
    }

    [Fact]
    public void RecoverOpenSessions_ShouldIgnoreClosedAndRecoveredSessions()
    {
        var closedSession = _store.BeginSession("GameModePilot").Value!;
        var closed = _store.Load(closedSession).Value!;
        _store.Save(closed.WithStatus(JournalStatus.Closed));

        var recoveredSession = _store.BeginSession("GameOptBox").Value!;
        var recovered = _store.Load(recoveredSession).Value!;
        _store.Save(recovered.WithStatus(JournalStatus.Recovered));

        var report = _service.RecoverOpenSessions();

        report.Value!.SessionsRecovered.Should().Be(0);
        report.Value!.EntriesRestored.Should().Be(0);
        _restorer.RestoredEntries.Should().BeEmpty();
    }

    [Fact]
    public void RecoverOpenSessions_WithNoSessions_ShouldReturnEmptyReport()
    {
        var report = _service.RecoverOpenSessions();

        report.IsSuccess.Should().BeTrue();
        report.Value!.SessionsRecovered.Should().Be(0);
        report.Value!.IsFullyRecovered.Should().BeTrue();
    }

    [Fact]
    public void RecoverOpenSessions_ShouldProcessMultipleSessions_OldestFirst()
    {
        var older = _store.BeginSession("GameModePilot").Value!;
        var newer = _store.BeginSession("GameModePilot").Value!;
        var baseTime = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        _store.AppendEntry(older, new JournalEntry("rule.older.action", 0, "{}", baseTime));
        _store.AppendEntry(newer, new JournalEntry("rule.newer.action", 0, "{}", baseTime.AddMinutes(5)));

        var report = _service.RecoverOpenSessions();

        report.Value!.SessionsRecovered.Should().Be(2);
        _restorer.RestoredEntries.Select(e => e.RuleId).Should().Equal("rule.older.action", "rule.newer.action");
    }

    /// <summary>
    /// 缺陷 E 回归证明：还原日志瞬时段不到时，开机恢复必须显式失败，
    /// 绝不能把"读不到"当成"没有待恢复会话"而静默报告完成（内核可还原承诺）。
    /// </summary>
    [Fact]
    public void RecoverOpenSessions_WhenJournalUnreadable_ShouldFail_NotReportDone()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        _store.AppendEntry(sessionId, new JournalEntry("svc.sysmain.disable", 0, "{}", DateTime.UtcNow));

        using (new FileStream(_store.GetSessionPath(sessionId), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var report = _service.RecoverOpenSessions();

            report.IsSuccess.Should().BeFalse("读不到还原日志时绝不能静默报告恢复完成");
            report.Error.Should().Contain("还原日志");
            _restorer.RestoredEntries.Should().BeEmpty();
        }
    }
}
