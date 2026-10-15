// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>规则应用协调器测试：危险二次确认 / 高危需输入确认 / 取消不生效。</summary>
public sealed class RuleActionCoordinatorTests
{
    private static TweakRule DangerRule(string id, string category = "隐私") => new()
    {
        Id = id,
        Category = category,
        Risk = RiskLevel.Danger,
        DisplayName = "危险规则",
        Description = "测试",
        ConsequenceHints = new[] { "后果一", "后果二" },
        Actions = new TweakAction[] { new FakeAction("d") },
    };

    private static (RuleRepository Repository, TweakEngine Engine) Build(TempDirectory temp)
    {
        var store = new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal"));
        var engine = new TweakEngine(
            new ITweakActionHandler[] { new FakeActionHandler() },
            store,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance);
        var repository = new RuleRepository(engine, string.Empty, null, NullLogger<RuleRepository>.Instance);
        return (repository, engine);
    }

    [Theory]
    [InlineData("privacy.defender.realtime.disable", true)]
    [InlineData("net.windowsupdate.auto.disable", true)]
    [InlineData("privacy.coreisolation.vbs.disable", true)]
    [InlineData("privacy.location.deny", false)]
    public void IsHighRisk_DetectsSensitiveRules(string id, bool expected)
    {
        RuleActionCoordinator.IsHighRisk(DangerRule(id)).Should().Be(expected);
    }

    [Fact]
    public void IsHighRisk_NoticeRule_IsFalse()
    {
        var rule = new TweakRule
        {
            Id = "game.timer.resolution",
            Category = "游戏",
            Risk = RiskLevel.Notice,
            DisplayName = "n",
            Actions = new TweakAction[] { new FakeAction("x") },
        };
        RuleActionCoordinator.IsHighRisk(rule).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_DangerRule_Confirmed_Applies()
    {
        using var temp = new TempDirectory();
        var (repository, engine) = Build(temp);
        var rule = DangerRule("privacy.defender.realtime.disable");
        engine.RegisterRule(rule).IsSuccess.Should().BeTrue();

        var dialogs = new FakeDialogService { Result = new(true, false) };
        var coordinator = new RuleActionCoordinator(repository, dialogs, new RecordingNotifier());

        var applied = await coordinator.ApplyAsync(rule);

        applied.Should().BeTrue();
        dialogs.Calls.Should().Be(1);
        dialogs.LastRequireTyped.Should().BeTrue("关闭 Defender 属高危操作，需输入确认");
        repository.IsApplied(rule.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Apply_DangerRule_Cancelled_NotApplied()
    {
        using var temp = new TempDirectory();
        var (repository, engine) = Build(temp);
        var rule = DangerRule("privacy.defender.realtime.disable");
        engine.RegisterRule(rule);

        var dialogs = new FakeDialogService { Result = new(false, false) };
        var notifier = new RecordingNotifier();
        var coordinator = new RuleActionCoordinator(repository, dialogs, notifier);

        var applied = await coordinator.ApplyAsync(rule);

        applied.Should().BeFalse();
        repository.IsApplied(rule.Id).Should().BeFalse();
        notifier.Messages.Should().Contain(message => message.Contains("已取消", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_SafeRule_SkipsConfirmation()
    {
        using var temp = new TempDirectory();
        var (repository, engine) = Build(temp);
        var rule = new TweakRule
        {
            Id = "latency.mouse.acceleration.disable",
            Category = "延迟",
            Risk = RiskLevel.Safe,
            DisplayName = "关闭鼠标加速",
            Description = "测试",
            Actions = new TweakAction[] { new FakeAction("s") },
        };
        engine.RegisterRule(rule);

        var dialogs = new FakeDialogService();
        var coordinator = new RuleActionCoordinator(repository, dialogs, new RecordingNotifier());

        var applied = await coordinator.ApplyAsync(rule);

        applied.Should().BeTrue();
        dialogs.Calls.Should().Be(0, "安全级规则无需二次确认");
        repository.IsApplied(rule.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Revert_AppliedRule_Succeeds()
    {
        using var temp = new TempDirectory();
        var (repository, engine) = Build(temp);
        var rule = new TweakRule
        {
            Id = "latency.mouse.acceleration.disable",
            Category = "延迟",
            Risk = RiskLevel.Safe,
            DisplayName = "关闭鼠标加速",
            Description = "测试",
            Actions = new TweakAction[] { new FakeAction("s") },
        };
        engine.RegisterRule(rule);
        engine.ApplyRule(rule.Id, RuleRepository.ProductName);

        var coordinator = new RuleActionCoordinator(repository, new FakeDialogService(), new RecordingNotifier());
        var reverted = await coordinator.RevertAsync(rule);

        reverted.Should().BeTrue();
        repository.IsApplied(rule.Id).Should().BeFalse();
    }
}
