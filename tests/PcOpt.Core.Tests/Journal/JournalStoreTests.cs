// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;

namespace PcOpt.Core.Tests.Journal;

/// <summary>
/// <see cref="JournalStore"/> 单元测试：原子写（.tmp + MoveFileEx 替换）、损坏文件容错、并发不产生半页文件。
/// </summary>
public sealed class JournalStoreTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly JournalStore _store;

    public JournalStoreTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "pcopt-journal-tests", Guid.NewGuid().ToString("N"));
        _store = new JournalStore(NullLogger<JournalStore>.Instance, _rootDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论
        }
    }

    [Fact]
    public void BeginSession_ShouldCreateOpenSessionFile()
    {
        var begin = _store.BeginSession("GameModePilot");

        begin.IsSuccess.Should().BeTrue();
        begin.Error.Should().BeEmpty();
        var sessionId = begin.Value!;
        sessionId.Should().NotBeEmpty();

        var path = _store.GetSessionPath(sessionId);
        File.Exists(path).Should().BeTrue();

        var load = _store.Load(sessionId);
        load.IsSuccess.Should().BeTrue();
        var journal = load.Value!;
        journal.SessionId.Should().Be(sessionId);
        journal.Product.Should().Be("GameModePilot");
        journal.Status.Should().Be(JournalStatus.Open);
        journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public void BeginSession_WithBlankProduct_ShouldFail()
    {
        var begin = _store.BeginSession("  ");

        begin.IsSuccess.Should().BeFalse();
        begin.Error.Should().Contain("产品名");
    }

    [Fact]
    public void AppendEntries_ShouldPersistAtomically_AndLeaveNoTempFiles()
    {
        var sessionId = _store.BeginSession("GameOptBox").Value!;
        var entries = new[]
        {
            new JournalEntry("svc.sysmain.disable", 0, "{\"startType\":2}", DateTime.UtcNow),
            new JournalEntry("reg.test.value", 1, "{\"exists\":false}", DateTime.UtcNow),
        };

        var append = _store.AppendEntries(sessionId, entries);

        append.IsSuccess.Should().BeTrue();
        var loaded = _store.Load(sessionId).Value!;
        loaded.Entries.Should().HaveCount(2);
        loaded.Entries[0].RuleId.Should().Be("svc.sysmain.disable");
        loaded.Entries[1].OriginalValueJson.Should().Be("{\"exists\":false}");

        // 原子写不得残留临时文件
        Directory.GetFiles(_rootDirectory).Should().HaveCount(1);
        Directory.GetFiles(_rootDirectory)[0].Should().EndWith(".json");
    }

    [Fact]
    public void Save_ShouldReplaceExistingFileAtomically()
    {
        var sessionId = _store.BeginSession("FrameLab").Value!;
        var journal = _store.Load(sessionId).Value!;
        journal.Entries.Add(new JournalEntry("pw.setactive.plan", 0, "{}", DateTime.UtcNow));

        var save = _store.Save(journal.WithStatus(JournalStatus.Closed));

        save.IsSuccess.Should().BeTrue();
        var reloaded = _store.Load(sessionId).Value!;
        reloaded.Status.Should().Be(JournalStatus.Closed);
        reloaded.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Load_NonExistentSession_ShouldReturnOkNull()
    {
        var load = _store.Load("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");

        load.IsSuccess.Should().BeTrue();
        load.Value.Should().BeNull();
    }

    [Fact]
    public void Load_CorruptedFile_ShouldFailWithClearError()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        File.WriteAllText(_store.GetSessionPath(sessionId), "{ 这不是合法 JSON ");

        var load = _store.Load(sessionId);

        load.IsSuccess.Should().BeFalse();
        load.Error.Should().Contain("损坏");
    }

    [Fact]
    public void ListSessions_ShouldSkipCorruptedFiles_AndReturnValidOnesOrdered()
    {
        var first = _store.BeginSession("GameModePilot").Value!;
        var second = _store.BeginSession("GameOptBox").Value!;
        File.WriteAllText(Path.Combine(_rootDirectory, "corrupted.json"), "not-json");

        var sessions = _store.ListSessions();

        sessions.Should().HaveCount(2);
        sessions.Select(s => s.SessionId).Should().Equal(first, second);
    }

    [Fact]
    public async Task ConcurrentWrites_ShouldNeverProduceCorruptFile()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        var readErrors = new List<string>();
        var stopReading = false;

        // 并发读：任何时刻读到的文件必须是合法 JSON（原子替换保证）
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stopReading))
            {
                try
                {
                    var text = File.ReadAllText(_store.GetSessionPath(sessionId));
                    using var document = JsonDocument.Parse(text);
                    _ = document.RootElement;
                }
                catch (JsonException ex)
                {
                    // 只有"读到不完整 JSON"才算违反原子写保证
                    lock (readErrors)
                    {
                        readErrors.Add(ex.Message);
                    }
                }
                catch (IOException)
                {
                    // MoveFileEx 原子替换瞬间存在共享冲突窗口，读侧可能瞬时失败后重试成功；
                    // 原子替换保证的是"读到的内容必然是完整页"，而非"任何时刻都读得到"。
                }
            }
        });

        // 并发写：8 个任务各追加 10 条
        var writers = Enumerable.Range(0, 8).Select(writerIndex => Task.Run(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                _store.AppendEntry(sessionId, new JournalEntry(
                    $"test.concurrent.entry{writerIndex}",
                    i,
                    "{}",
                    DateTime.UtcNow));
            }
        })).ToArray();

        await Task.WhenAll(writers);
        Volatile.Write(ref stopReading, true);
        await reader;

        readErrors.Should().BeEmpty("原子写保证任何时刻文件都是完整页");
        var loaded = _store.Load(sessionId).Value!;
        loaded.Entries.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetSessionPath_ShouldRejectIllegalSessionId()
    {
        var act = () => _store.GetSessionPath("../../etc/passwd");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultRootDirectory_ShouldBeProgramDataPcoptJournal()
    {
        JournalStore.DefaultRootDirectory.Should().Contain("pcopt");
        JournalStore.DefaultRootDirectory.Should().EndWith("journal");
    }

    [Fact]
    public void JournalRoundTrip_ShouldPreserveTimestampsAndRevertMark()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        var appliedAt = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        _store.AppendEntry(sessionId, new JournalEntry("svc.sysmain.disable", 0, "{}", appliedAt));

        var journal = _store.Load(sessionId).Value!;
        var reverted = journal.Entries[0].WithReverted(appliedAt.AddMinutes(30));
        reverted.RevertedAt.Should().Be(appliedAt.AddMinutes(30));
        reverted.IsOpen.Should().BeFalse();

        var updated = journal.WithStatus(JournalStatus.Closed);
        updated.Entries[0] = reverted;
        _store.Save(updated);

        var reloaded = _store.Load(sessionId).Value!;
        reloaded.Entries[0].AppliedAt.Should().Be(appliedAt);
        reloaded.Entries[0].RevertedAt.Should().Be(appliedAt.AddMinutes(30));
        reloaded.Status.Should().Be(JournalStatus.Closed);
    }

    // ---------- 严格 / 宽松读取语义（缺陷 E） ----------

    /// <summary>损坏 JSON 文件在严格模式下仍应跳过并成功返回（无法解析的文件不含可用信息）。</summary>
    [Fact]
    public void TryListSessions_WithCorruptFile_ShouldSkipAndSucceed()
    {
        var valid = _store.BeginSession("GameOptBox").Value!;
        File.WriteAllText(Path.Combine(_rootDirectory, "broken.json"), "{ 这不是合法 JSON ");

        var strict = _store.TryListSessions();

        strict.IsSuccess.Should().BeTrue(strict.Error);
        strict.Value.Should().ContainSingle().Which.SessionId.Should().Be(valid);
    }

    /// <summary>
    /// 关键路径语义：文件读不到（排他句柄 / 共享冲突）时严格模式必须失败，
    /// 而宽松模式（展示用）跳过读不到的文件、不抛错。缺陷 E 的核心防线。
    /// </summary>
    [Fact]
    public void TryListSessions_WhenFileLocked_ShouldFail_WhileLenientSkips()
    {
        var sessionId = _store.BeginSession("GameModePilot").Value!;
        var path = _store.GetSessionPath(sessionId);

        // 独占占用：模拟排他句柄 / 杀软实时扫描导致的持续读失败
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var strict = _store.TryListSessions();
            strict.IsSuccess.Should().BeFalse("I/O 错误绝不能被静默吞成空会话");
            strict.Error.Should().Contain("还原日志");

            // 宽松扫描用于展示路径：跳过读不到的文件而不是抛错
            _store.ListSessions().Should().BeEmpty();
        }

        // 释放句柄后严格读取应恢复成功（证明仅瞬时不可读）
        _store.TryListSessions().Value.Should().ContainSingle().Which.SessionId.Should().Be(sessionId);
    }

    /// <summary>损坏文件在宽松模式下同样应跳过，不影响其他会话（保持既有行为不回归）。</summary>
    [Fact]
    public void ListSessions_ShouldNotBeBlocked_ByCorruptFile()
    {
        var valid = _store.BeginSession("FrameLab").Value!;
        File.WriteAllText(Path.Combine(_rootDirectory, "garbage.json"), "nope");

        var sessions = _store.ListSessions();

        sessions.Should().ContainSingle().Which.SessionId.Should().Be(valid);
    }
}
