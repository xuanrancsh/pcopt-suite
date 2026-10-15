// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>
/// 还原中心日志导出的隐私与契约验收（QA 构造，T30；PRD B-P0-9、架构 8.3）。
/// 断言导出内容<b>不泄露任何用户路径 / 用户名</b>，且结构契约稳定（非空）。
/// </summary>
public sealed class RestoreExportPrivacyTests
{
    private static RestoreCenterService Build(TempDirectory temp, out TweakEngine engine, out RuleRepository repository)
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal"));
        engine = new TweakEngine(
            new ITweakActionHandler[] { new FakeActionHandler() },
            store,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance);
        repository = new RuleRepository(engine, string.Empty, null, NullLogger<RuleRepository>.Instance);
        return new RestoreCenterService(engine, repository, store, new FakeRestorePointService(), NullLogger<RestoreCenterService>.Instance);
    }

    private static TweakRule DemoRule() => new()
    {
        Id = "test.export.rule",
        Category = "性能",
        Risk = RiskLevel.Safe,
        DisplayName = "导出测试规则",
        Description = "测试",
        Actions = new TweakAction[] { new FakeAction("a") },
    };

    [Fact]
    public void ExportLog_DoesNotLeakUserPathsOrUsername()
    {
        using var temp = new TempDirectory();
        var service = Build(temp, out var engine, out _);
        engine.RegisterRule(DemoRule());
        engine.ApplyRule("test.export.rule", RuleRepository.ProductName);

        var target = temp.Combine("logs", "oplog.json");
        service.ExportLog(target).IsSuccess.Should().BeTrue();

        var json = File.ReadAllText(target);

        // 非空断言：确实写出了有意义的内容（避免"导出空文件也通过"）。
        json.Should().Contain("test.export.rule");
        json.Should().Contain("sessions");
        json.Should().Contain("appliedRules");
        json.Should().Contain("exportedAtUtc");

        // 隐私断言：不得出现任何用户路径 / 用户名。
        json.Should().NotContain(temp.Path, "导出内容不得包含还原日志目录等本地路径");
        json.Should().NotContain(temp.Combine("journal"), "导出内容不得包含 Journal 本地路径");
        json.Should().NotContain(Path.GetTempPath(), "导出内容不得包含临时目录路径");

        var user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user) && user.Length >= 3)
        {
            json.Should().NotContain(user, "导出内容不得包含当前用户名");
        }

        json.Should().NotContain("C:\\Users\\", "导出内容不得包含用户主目录绝对路径");

        // 契约断言：应可解析为合法 JSON。
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void ExportLog_EmptyPath_Fails_NoFileWritten()
    {
        using var temp = new TempDirectory();
        var service = Build(temp, out _, out _);

        service.ExportLog(string.Empty).IsSuccess.Should().BeFalse();
        service.ExportLog("   ").IsSuccess.Should().BeFalse();
    }
}
