// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using PcOpt.Core.Journal;
using PcOpt.Core.Platform;
using PcOpt.Core.Tests.TestDoubles;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.Tweak;

/// <summary>
/// 合规红线 #9 测试（PRD 2026-10-14 用户确认）：
/// 官方签名规则包不得包含不可自动还原的动作（Shell / powercfg 删除计划）；
/// 自定义（未签名）规则允许使用，由 UI 层标注 + ConfirmDialog 把关。
/// </summary>
public sealed class OfficialRulePolicyTests
{
    private const string ShellRuleYaml = """
        id: exp.shell.action
        category: 体验
        risk: danger
        displayName: 执行清理命令
        actions:
          - type: Shell
            command: cleanmgr.exe
            arguments: /verylowdisk
        """;

    private const string PowerDeleteRuleYaml = """
        id: exp.power.delete
        category: 性能
        risk: notice
        displayName: 删除电源计划
        actions:
          - type: PowerCfg
            kind: Delete
            argument: 381b4222-f694-41f0-9685-ff5bb260df2e
        """;

    private const string RevertibleRuleYaml = """
        id: svc.sysmain.disable
        category: 性能
        risk: safe
        displayName: 关闭 SysMain 服务
        actions:
          - type: ServiceStartType
            serviceName: SysMain
            target: Disabled
          - type: PowerCfg
            kind: SetActive
            argument: e9a42b02-d5df-448d-aa00-03f14749eb61
        """;

    // ---------- 策略单测（不涉及签名） ----------

    [Theory]
    [InlineData(true, "Shell")]
    [InlineData(true, "PowerDelete")]
    [InlineData(false, "RegistrySet")]
    [InlineData(false, "ServiceStartType")]
    [InlineData(false, "ScheduledTask")]
    [InlineData(false, "ProcessPriority")]
    [InlineData(false, "TimerResolution")]
    [InlineData(false, "PowerSetActive")]
    [InlineData(false, "PowerImport")]
    [InlineData(false, "PowerList")]
    public void IsAutoRevertible_ShouldMatchHandlerRevertSemantics(bool expectedNonRevertible, string kind)
    {
        TweakAction action = kind switch
        {
            "Shell" => new ShellAction("cmd.exe", "/c echo"),
            "PowerDelete" => new PowerCfgAction(PowerActionKind.Delete, "381b4222-f694-41f0-9685-ff5bb260df2e"),
            "RegistrySet" => new RegistrySetAction(@"HKLM\SYSTEM\Test", "Start", 4, RegistryValueKind.DWord),
            "ServiceStartType" => new ServiceStartTypeAction("TestService", ServiceStartMode.Manual),
            "ScheduledTask" => new ScheduledTaskAction(@"\Microsoft\Windows\Test", false),
            "ProcessPriority" => new ProcessPriorityAction("game.exe", ProcessPriorityClass.High, null),
            "TimerResolution" => new TimerResolutionAction(5000),
            "PowerSetActive" => new PowerCfgAction(PowerActionKind.SetActive, "e9a42b02-d5df-448d-aa00-03f14749eb61"),
            "PowerImport" => new PowerCfgAction(PowerActionKind.Import, "C:\\plan.pow"),
            "PowerList" => new PowerCfgAction(PowerActionKind.List, string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        OfficialRulePolicy.IsAutoRevertible(action).Should().Be(!expectedNonRevertible);
    }

    [Fact]
    public void ValidateRule_RevertibleRule_ShouldPass()
    {
        var rule = TweakRuleParser.Parse(RevertibleRuleYaml).Value!;
        var result = OfficialRulePolicy.ValidateRule(rule);

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public void ValidateRule_ShellAction_ShouldFailWithActionName()
    {
        var rule = TweakRuleParser.Parse(ShellRuleYaml).Value!;
        var result = OfficialRulePolicy.ValidateRule(rule);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Shell");
        result.Error.Should().Contain("红线 #9");
    }

    [Fact]
    public void ValidateRule_PowerCfgDelete_ShouldFail()
    {
        var rule = TweakRuleParser.Parse(PowerDeleteRuleYaml).Value!;
        var result = OfficialRulePolicy.ValidateRule(rule);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("电源计划");
    }

    [Fact]
    public void ValidatePackage_MultipleViolations_ShouldListAll()
    {
        var rules = new[]
        {
            TweakRuleParser.Parse(ShellRuleYaml).Value!,
            TweakRuleParser.Parse(PowerDeleteRuleYaml).Value!,
            TweakRuleParser.Parse(RevertibleRuleYaml).Value!,
        };

        var result = OfficialRulePolicy.ValidatePackage(rules);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("exp.shell.action");
        result.Error.Should().Contain("exp.power.delete");
        result.Error.Should().NotContain("svc.sysmain.disable"); // 合规规则不出现在错误里
    }

    // ---------- 引擎集成：签名包拒绝 / 未签名放行 ----------

    [Fact]
    public void LoadRulePackage_SignedShellRule_ShouldBeRejected_AndNotRegistered()
    {
        var content = Encoding.UTF8.GetBytes(ShellRuleYaml);
        var (privateKey, publicKey, signature) = TestKeys.CreateAndSign(content);
        var engine = CreateEngine(publicKey);

        var load = engine.LoadRulePackage(content, signature);

        load.IsSuccess.Should().BeFalse();
        load.Error.Should().Contain("红线 #9");
        engine.LoadedRules.Should().BeEmpty();
    }

    [Fact]
    public void LoadRulePackage_SignedPowerDeleteRule_ShouldBeRejected()
    {
        var content = Encoding.UTF8.GetBytes(PowerDeleteRuleYaml);
        var (_, publicKey, signature) = TestKeys.CreateAndSign(content);
        var engine = CreateEngine(publicKey);

        var load = engine.LoadRulePackage(content, signature);

        load.IsSuccess.Should().BeFalse();
        load.Error.Should().Contain("红线 #9");
    }

    [Fact]
    public void LoadRulePackage_SignedRevertibleRule_ShouldLoad()
    {
        var content = Encoding.UTF8.GetBytes(RevertibleRuleYaml);
        var (_, publicKey, signature) = TestKeys.CreateAndSign(content);
        var engine = CreateEngine(publicKey);

        var load = engine.LoadRulePackage(content, signature);

        load.IsSuccess.Should().BeTrue(load.Error);
        load.Value!.Id.Should().Be("svc.sysmain.disable");
    }

    [Fact]
    public void LoadRuleFromYaml_UnsignedShellRule_ShouldBeAllowed()
    {
        // 自定义（未签名）规则允许含不可还原动作；UI 层负责标注 + 危险确认
        var engine = CreateEngine();

        var load = engine.LoadRuleFromYaml(ShellRuleYaml);

        load.IsSuccess.Should().BeTrue(load.Error);
        engine.LoadedRules.Should().ContainSingle(r => r.Id == "exp.shell.action");
    }

    [Fact]
    public void LoadRuleFromYaml_UnsignedPowerDeleteRule_ShouldBeAllowed()
    {
        var engine = CreateEngine();

        var load = engine.LoadRuleFromYaml(PowerDeleteRuleYaml);

        load.IsSuccess.Should().BeTrue(load.Error);
    }

    private static TweakEngine CreateEngine(byte[]? publicKey = null)
    {
        var verifier = publicKey is null
            ? null
            : RuleSignatureVerifier.Create(publicKey).Value!;
        var store = new JournalStore(
            NullLogger<JournalStore>.Instance,
            Path.Combine(Path.GetTempPath(), "pcopt-policy-tests", Guid.NewGuid().ToString("N")));
        return new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            store,
            TestSystemEnvironment.Win11X64(),
            NullLogger<TweakEngine>.Instance,
            verifier);
    }
}
