// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Win32;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.Tweak;

/// <summary>
/// <see cref="TweakRuleParser"/> 单元测试：YAML → 规则模型映射、字段校验、错误信息明确性。
/// </summary>
public sealed class TweakRuleParserTests
{
    private const string ValidYaml = """
        id: svc.sysmain.disable
        category: 性能
        risk: safe
        displayName: 让游戏少抢 CPU 的后台服务
        description: 关闭 SysMain（Superfetch）服务
        consequenceHints:
          - 可能影响打印机、蓝牙等周边响应
          - 开机初期首次打开程序会略慢
        conditions:
          - osName: Windows
            minBuild: 19045
          - cpuArchitecture: X64
        actions:
          - type: ServiceStartType
            serviceName: SysMain
            target: Disabled
          - type: RegistrySet
            path: HKLM\SOFTWARE\pcopt-test
            name: Start
            value: 4
            valueKind: DWord
          - type: RegistrySet
            path: HKCU\Software\pcopt-test
            name: Multi
            value: [alpha, beta]
            valueKind: MultiString
          - type: RegistrySet
            path: HKCU\Software\pcopt-test
            name: Blob
            value: AQIDBA==
            valueKind: Binary
          - type: PowerCfg
            kind: SetActive
            argument: e9a42b2d-d502-4f9c-a3f9-53e1e0e6b4b0
          - type: ProcessPriority
            processName: cs2.exe
            priority: High
            affinityMask: "0x3F"
          - type: TimerResolution
            desiredResolution100Ns: 5000
          - type: Shell
            command: cmd.exe
            arguments: /c echo hello
          - type: ScheduledTask
            taskPath: \pcopt-test\sample-task
            enable: false
        """;

    [Fact]
    public void Parse_ValidRule_ShouldMapAllFields()
    {
        var result = TweakRuleParser.Parse(ValidYaml);

        result.IsSuccess.Should().BeTrue(result.Error);
        var rule = result.Value!;

        rule.Id.Should().Be("svc.sysmain.disable");
        rule.Category.Should().Be("性能");
        rule.Risk.Should().Be(RiskLevel.Safe);
        rule.DisplayName.Should().Be("让游戏少抢 CPU 的后台服务");
        rule.Description.Should().Contain("SysMain");
        rule.ConsequenceHints.Should().HaveCount(2);
        rule.Conditions.Should().HaveCount(2);
        rule.Conditions[0].OsName.Should().Be("Windows");
        rule.Conditions[0].MinBuild.Should().Be(19045);
        rule.Conditions[1].CpuArchitecture.Should().Be("X64");
        rule.Actions.Should().HaveCount(9);
    }

    [Fact]
    public void Parse_ValidRule_ShouldMapAllActionTypes()
    {
        var rule = TweakRuleParser.Parse(ValidYaml).Value!;

        var service = rule.Actions[0].Should().BeOfType<ServiceStartTypeAction>().Subject;
        service.ServiceName.Should().Be("SysMain");
        service.Target.Should().Be(ServiceStartMode.Disabled);

        var dword = rule.Actions[1].Should().BeOfType<RegistrySetAction>().Subject;
        dword.Path.Should().Be(@"HKLM\SOFTWARE\pcopt-test");
        dword.Name.Should().Be("Start");
        dword.Kind.Should().Be(RegistryValueKind.DWord);
        Convert.ToInt32(dword.Value).Should().Be(4);

        var multi = rule.Actions[2].Should().BeOfType<RegistrySetAction>().Subject;
        multi.Kind.Should().Be(RegistryValueKind.MultiString);
        multi.Value.Should().BeOfType<string[]>().Which.Should().Equal("alpha", "beta");

        var binary = rule.Actions[3].Should().BeOfType<RegistrySetAction>().Subject;
        binary.Kind.Should().Be(RegistryValueKind.Binary);
        binary.Value.Should().BeOfType<byte[]>().Which.Should().Equal(new byte[] { 1, 2, 3, 4 });

        var power = rule.Actions[4].Should().BeOfType<PowerCfgAction>().Subject;
        power.Kind.Should().Be(PowerActionKind.SetActive);
        power.Argument.Should().Be("e9a42b2d-d502-4f9c-a3f9-53e1e0e6b4b0");

        var process = rule.Actions[5].Should().BeOfType<ProcessPriorityAction>().Subject;
        process.ProcessName.Should().Be("cs2.exe");
        process.Priority.Should().Be(ProcessPriorityClass.High);
        process.AffinityMask.Should().Be(0x3F);

        var timer = rule.Actions[6].Should().BeOfType<TimerResolutionAction>().Subject;
        timer.DesiredResolution100Ns.Should().Be(5000);

        var shell = rule.Actions[7].Should().BeOfType<ShellAction>().Subject;
        shell.Command.Should().Be("cmd.exe");
        shell.Arguments.Should().Be("/c echo hello");

        var task = rule.Actions[8].Should().BeOfType<ScheduledTaskAction>().Subject;
        task.TaskPath.Should().Be(@"\pcopt-test\sample-task");
        task.Enable.Should().BeFalse();
    }

    [Fact]
    public void Parse_QWordValue_ShouldMapToLong()
    {
        var yaml = """
            id: reg.test.qword
            category: 性能
            risk: Notice
            displayName: QWord 测试
            actions:
              - type: RegistrySet
                path: HKCU\Software\pcopt-test
                name: Big
                value: 9007199254740993
                valueKind: QWord
            """;

        var rule = TweakRuleParser.Parse(yaml).Value!;
        var action = rule.Actions[0].Should().BeOfType<RegistrySetAction>().Subject;
        action.Kind.Should().Be(RegistryValueKind.QWord);
        Convert.ToInt64(action.Value).Should().Be(9007199254740993L);
    }

    [Fact]
    public void Parse_MissingId_ShouldFail()
    {
        var result = TweakRuleParser.Parse("category: 性能\nrisk: safe\ndisplayName: x\nactions:\n  - type: Shell\n    command: cmd.exe\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("id");
    }

    [Fact]
    public void Parse_MissingCategory_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: a.b.c\nrisk: safe\ndisplayName: x\nactions:\n  - type: Shell\n    command: cmd.exe\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("category");
    }

    [Fact]
    public void Parse_InvalidRisk_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: a.b.c\ncategory: 性能\nrisk: 无所谓\ndisplayName: x\nactions:\n  - type: Shell\n    command: cmd.exe\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("risk");
    }

    [Fact]
    public void Parse_InvalidRuleIdFormat_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: 只有一段\ncategory: 性能\nrisk: safe\ndisplayName: x\nactions:\n  - type: Shell\n    command: cmd.exe\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("分类.对象.动作");
    }

    [Fact]
    public void Parse_MissingDisplayName_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: a.b.c\ncategory: 性能\nrisk: safe\nactions:\n  - type: Shell\n    command: cmd.exe\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("displayName");
    }

    [Fact]
    public void Parse_TooManyConsequenceHints_ShouldFail()
    {
        var result = TweakRuleParser.Parse("""
            id: a.b.c
            category: 性能
            risk: safe
            displayName: x
            consequenceHints: [一, 二, 三, 四]
            actions:
              - type: Shell
                command: cmd.exe
            """);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("最多 3 条");
    }

    [Fact]
    public void Parse_EmptyActions_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: a.b.c\ncategory: 性能\nrisk: safe\ndisplayName: x\nactions: []\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("至少一个动作");
    }

    [Fact]
    public void Parse_UnknownActionType_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: a.b.c\ncategory: 性能\nrisk: safe\ndisplayName: x\nactions:\n  - type: Teleport\n    target: moon\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("类型未知");
    }

    [Fact]
    public void Parse_InvalidAffinityMask_ShouldFail()
    {
        var result = TweakRuleParser.Parse("""
            id: a.b.c
            category: 性能
            risk: safe
            displayName: x
            actions:
              - type: ProcessPriority
                processName: cs2.exe
                priority: High
                affinityMask: "0xZZ"
            """);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("affinityMask");
    }

    [Fact]
    public void Parse_InvalidYamlSyntax_ShouldFail()
    {
        var result = TweakRuleParser.Parse("id: [unclosed\n");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("YAML 解析失败");
    }

    [Fact]
    public void Parse_EmptyContent_ShouldFail()
    {
        var result = TweakRuleParser.Parse("   ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("为空");
    }

    [Fact]
    public void Parse_InvalidBuildNumber_ShouldFail()
    {
        var result = TweakRuleParser.Parse("""
            id: a.b.c
            category: 性能
            risk: safe
            displayName: x
            conditions:
              - minBuild: 不是数字
            actions:
              - type: Shell
                command: cmd.exe
            """);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("minBuild");
    }
}
