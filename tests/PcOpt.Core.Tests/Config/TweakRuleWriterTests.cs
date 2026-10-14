// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using Microsoft.Win32;
using PcOpt.Core.Config;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Tests.Config;

/// <summary>
/// <see cref="TweakRuleWriter"/> round-trip 测试（T10，架构 3.5）：
/// Write → TweakRuleParser.Parse 必须语义等价（全字段 + 全动作类型 + 嵌套 OsCondition 边界）。
/// </summary>
public sealed class TweakRuleWriterTests
{
    /// <summary>构造一条覆盖全部字段的规则（每种动作类型各一）。</summary>
    private static TweakRule BuildFullRule() => new()
    {
        Id = "perf.sysmain.disable",
        Category = "性能",
        Risk = RiskLevel.Notice,
        DisplayName = "关闭 SysMain 服务减少磁盘占用",
        Description = "SysMain 会把常用文件预加载进内存，机械硬盘上可能造成磁盘忙碌。",
        ConsequenceHints = new[] { "部分程序首次启动可能稍慢", "内存占用会略有下降" },
        Conditions = new[]
        {
            new OsCondition
            {
                OsName = "Windows",
                MinBuild = 22000,
                CpuArchitecture = "X64",
            },
            new OsCondition
            {
                GpuVendor = "NVIDIA",
                MaxBuild = 99999,
            },
        },
        Actions = new TweakAction[]
        {
            new RegistrySetAction(
                @"HKLM\SYSTEM\CurrentControlSet\Services\SysMain",
                "Start",
                4,
                RegistryValueKind.DWord),
            new ServiceStartTypeAction("SysMain", ServiceStartMode.Disabled),
            new ScheduledTaskAction(@"\Microsoft\Windows\ApplicationExperience\Microsoft Compatibility Appraiser", false),
            new PowerCfgAction(PowerActionKind.SetActive, "e9a42b02-d5df-448d-aa00-03f14749eb61"),
            new ProcessPriorityAction("game.exe", ProcessPriorityClass.AboveNormal, 0b1111),
            new TimerResolutionAction(5000),
            new ShellAction("cmd.exe", "/c echo done"),
        },
    };

    /// <summary>
    /// 动作集合深比较。<see cref="TweakAction"/> 是无成员抽象 record 基类，
    /// FluentAssertions 默认按期望方声明类型发现成员会抛 "No members were found for comparison"，
    /// 必须 <c>RespectingRuntimeTypes</c> 按运行时类型（各具体 record）逐字段深比较，
    /// 同时 byte[] / string[] 等集合成员可正确深比较，规避 record 引用相等问题。
    /// </summary>
    private static void AssertActionsEquivalent(IReadOnlyList<TweakAction> actual, IReadOnlyList<TweakAction> expected) =>
        actual.Should().BeEquivalentTo(expected, options => options.RespectingRuntimeTypes());

    [Fact]
    public void Write_FullRule_ShouldRoundTripThroughParser()
    {
        var rule = BuildFullRule();
        var yaml = TweakRuleWriter.Write(rule);

        var parsed = TweakRuleParser.Parse(yaml);
        parsed.IsSuccess.Should().BeTrue(parsed.Error);

        var result = parsed.Value!;
        result.Id.Should().Be(rule.Id);
        result.Category.Should().Be(rule.Category);
        result.Risk.Should().Be(rule.Risk);
        result.DisplayName.Should().Be(rule.DisplayName);
        result.Description.Should().Be(rule.Description);
        result.ConsequenceHints.Should().Equal(rule.ConsequenceHints);
        result.Conditions.Should().BeEquivalentTo(rule.Conditions); // record 值相等
        AssertActionsEquivalent(result.Actions, rule.Actions);
    }

    [Fact]
    public void Write_MinimalRule_ShouldRoundTrip()
    {
        var rule = new TweakRule
        {
            Id = "net.tcp.autotuning.disable",
            Category = "网络",
            Risk = RiskLevel.Safe,
            DisplayName = "关闭 TCP 自动调谐",
            Actions = new TweakAction[]
            {
                new RegistrySetAction(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TCPNoDelay", 1, RegistryValueKind.DWord),
            },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);

        var result = parsed.Value!;
        result.Description.Should().BeEmpty();
        result.ConsequenceHints.Should().BeEmpty();
        result.Conditions.Should().BeEmpty();
        AssertActionsEquivalent(result.Actions, rule.Actions);
    }

    [Theory]
    [InlineData(RegistryValueKind.String, "hello")]
    [InlineData(RegistryValueKind.ExpandString, "%SystemRoot%\\System32")]
    [InlineData(RegistryValueKind.DWord, 4)]
    [InlineData(RegistryValueKind.QWord, 9007199254740993L)]
    public void Write_RegistrySetScalarKinds_ShouldRoundTrip(RegistryValueKind kind, object value)
    {
        var rule = new TweakRule
        {
            Id = "test.registry.scalar",
            Category = "性能",
            Risk = RiskLevel.Safe,
            DisplayName = "标量注册表值往返",
            Actions = new TweakAction[] { new RegistrySetAction(@"HKCU\Software\pcopt-test", "value", value, kind) },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Fact]
    public void Write_RegistrySetBinary_ShouldRoundTripAsBase64()
    {
        byte[] bytes = { 0x00, 0x01, 0xFE, 0xFF, 0x10 };
        var rule = new TweakRule
        {
            Id = "test.registry.binary",
            Category = "性能",
            Risk = RiskLevel.Safe,
            DisplayName = "二进制注册表值往返",
            Actions = new TweakAction[] { new RegistrySetAction(@"HKCU\Software\pcopt-test", "blob", bytes, RegistryValueKind.Binary) },
        };

        var yaml = TweakRuleWriter.Write(rule);
        yaml.Should().Contain("valueKind: Binary");

        var parsed = TweakRuleParser.Parse(yaml);
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Fact]
    public void Write_RegistrySetMultiString_ShouldRoundTripAsList()
    {
        var rule = new TweakRule
        {
            Id = "test.registry.multistring",
            Category = "性能",
            Risk = RiskLevel.Safe,
            DisplayName = "多字符串注册表值往返",
            Actions = new TweakAction[]
            {
                new RegistrySetAction(
                    @"HKLM\SYSTEM\CurrentControlSet\Services\Test",
                    "DependOnService",
                    new[] { "RpcSs", "LanmanWorkstation" },
                    RegistryValueKind.MultiString),
            },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Theory]
    [InlineData(ServiceStartMode.Boot)]
    [InlineData(ServiceStartMode.System)]
    [InlineData(ServiceStartMode.Automatic)]
    [InlineData(ServiceStartMode.Manual)]
    [InlineData(ServiceStartMode.Disabled)]
    public void Write_ServiceStartType_AllModesShouldRoundTrip(ServiceStartMode mode)
    {
        var rule = new TweakRule
        {
            Id = "test.service.mode",
            Category = "性能",
            Risk = RiskLevel.Safe,
            DisplayName = "服务启动类型往返",
            Actions = new TweakAction[] { new ServiceStartTypeAction("TestService", mode) },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Write_ScheduledTask_EnableFlagShouldRoundTrip(bool enable)
    {
        var rule = new TweakRule
        {
            Id = "test.scheduledtask.enable",
            Category = "体验",
            Risk = RiskLevel.Notice,
            DisplayName = "计划任务开关往返",
            Actions = new TweakAction[] { new ScheduledTaskAction(@"\Microsoft\Windows\Test", enable) },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Theory]
    [InlineData(PowerActionKind.List)]
    [InlineData(PowerActionKind.Import, "C:\\plans\\ultimate.pow")]
    [InlineData(PowerActionKind.SetActive, "e9a42b02-d5df-448d-aa00-03f14749eb61")]
    [InlineData(PowerActionKind.Delete, "381b4222-f694-41f0-9685-ff5bb260df2e")]
    public void Write_PowerCfg_AllKindsShouldRoundTrip(PowerActionKind kind, string argument = "")
    {
        var rule = new TweakRule
        {
            Id = "test.powercfg.action",
            Category = "性能",
            Risk = RiskLevel.Notice,
            DisplayName = "电源计划操作往返",
            Actions = new TweakAction[] { new PowerCfgAction(kind, argument) },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Fact]
    public void Write_ProcessPriority_WithAndWithoutAffinityShouldRoundTrip()
    {
        var withMask = new TweakRule
        {
            Id = "test.priority.withmask",
            Category = "游戏",
            Risk = RiskLevel.Notice,
            DisplayName = "带亲和性掩码往返",
            Actions = new TweakAction[] { new ProcessPriorityAction("game.exe", ProcessPriorityClass.High, 0x0F) },
        };
        var withoutMask = new TweakRule
        {
            Id = "test.priority.withoutmask",
            Category = "游戏",
            Risk = RiskLevel.Notice,
            DisplayName = "不带亲和性掩码往返",
            Actions = new TweakAction[] { new ProcessPriorityAction("game.exe", ProcessPriorityClass.Idle, null) },
        };

        AssertActionsEquivalent(TweakRuleParser.Parse(TweakRuleWriter.Write(withMask)).Value!.Actions, withMask.Actions);
        AssertActionsEquivalent(TweakRuleParser.Parse(TweakRuleWriter.Write(withoutMask)).Value!.Actions, withoutMask.Actions);
    }

    [Theory]
    [InlineData(5000u)]
    [InlineData(10000u)]
    [InlineData(156250u)]
    public void Write_TimerResolution_ShouldRoundTrip(uint resolution)
    {
        var rule = new TweakRule
        {
            Id = "test.timer.resolution",
            Category = "延迟",
            Risk = RiskLevel.Safe,
            DisplayName = "计时器分辨率往返",
            Actions = new TweakAction[] { new TimerResolutionAction(resolution) },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
    }

    [Fact]
    public void Write_ShellAction_WithAndWithoutArgumentsShouldRoundTrip()
    {
        var withArgs = new TweakRule
        {
            Id = "test.shell.withargs",
            Category = "体验",
            Risk = RiskLevel.Danger,
            DisplayName = "带参数 Shell 往返",
            Actions = new TweakAction[] { new ShellAction("powercfg.exe", "-energy") },
        };
        var withoutArgs = new TweakRule
        {
            Id = "test.shell.withoutargs",
            Category = "体验",
            Risk = RiskLevel.Danger,
            DisplayName = "不带参数 Shell 往返",
            Actions = new TweakAction[] { new ShellAction("cleanmgr.exe", string.Empty) },
        };

        AssertActionsEquivalent(TweakRuleParser.Parse(TweakRuleWriter.Write(withArgs)).Value!.Actions, withArgs.Actions);
        AssertActionsEquivalent(TweakRuleParser.Parse(TweakRuleWriter.Write(withoutArgs)).Value!.Actions, withoutArgs.Actions);
    }

    [Fact]
    public void Write_NestedOsCondition_PartialFieldsShouldRoundTrip()
    {
        // 每个条件只设置部分字段：null 字段写入时必须整体省略，读回后仍为 null
        var conditions = new[]
        {
            new OsCondition { OsName = "Windows" },
            new OsCondition { MinBuild = 22000 },
            new OsCondition { MaxBuild = 19045 },
            new OsCondition { CpuArchitecture = "Arm64" },
            new OsCondition { GpuVendor = "AMD" },
            new OsCondition { OsName = "Windows", MinBuild = 17763, MaxBuild = 20348, CpuArchitecture = "X86", GpuVendor = "Intel" },
        };

        var rule = new TweakRule
        {
            Id = "test.condition.partial",
            Category = "性能",
            Risk = RiskLevel.Safe,
            DisplayName = "嵌套条件部分字段往返",
            Conditions = conditions,
            Actions = new TweakAction[] { new TimerResolutionAction(5000) },
        };

        var parsed = TweakRuleParser.Parse(TweakRuleWriter.Write(rule));
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        parsed.Value!.Conditions.Should().BeEquivalentTo(conditions);
    }

    [Fact]
    public void Write_SameRuleTwice_ShouldProduceIdenticalOutput()
    {
        var rule = BuildFullRule();
        TweakRuleWriter.Write(rule).Should().Be(TweakRuleWriter.Write(rule));
    }

    [Fact]
    public void Write_Output_ShouldUseDocumentedCamelCaseKeys()
    {
        var yaml = TweakRuleWriter.Write(BuildFullRule());

        yaml.Should().Contain("id: perf.sysmain.disable");
        yaml.Should().Contain("displayName:");
        yaml.Should().Contain("consequenceHints:");
        yaml.Should().Contain("conditions:");
        yaml.Should().Contain("actions:");
        yaml.Should().Contain("type: RegistrySet");
        yaml.Should().Contain("type: ServiceStartType");
        yaml.Should().Contain("type: ScheduledTask");
        yaml.Should().Contain("type: PowerCfg");
        yaml.Should().Contain("type: ProcessPriority");
        yaml.Should().Contain("type: TimerResolution");
        yaml.Should().Contain("type: Shell");
    }

    [Fact]
    public void WriteToFile_ValidPath_ShouldWriteAndRoundTripFromFile()
    {
        var rule = BuildFullRule();
        var path = Path.Combine(Path.GetTempPath(), "pcopt-writer-tests", Guid.NewGuid().ToString("N"), "rule.yaml");

        try
        {
            var write = TweakRuleWriter.WriteToFile(rule, path);
            write.IsSuccess.Should().BeTrue(write.Error);

            var parsed = TweakRuleParser.Parse(File.ReadAllText(path));
            parsed.IsSuccess.Should().BeTrue(parsed.Error);
            AssertActionsEquivalent(parsed.Value!.Actions, rule.Actions);
            parsed.Value!.Conditions.Should().BeEquivalentTo(rule.Conditions);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteToFile_EmptyPath_ShouldFailWithClearMessage()
    {
        var result = TweakRuleWriter.WriteToFile(BuildFullRule(), "  ");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("路径为空");
    }

    [Fact]
    public void Write_NullRule_ShouldThrowArgumentNullException()
    {
        var act = () => TweakRuleWriter.Write(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Write_UnknownActionType_ShouldThrow()
    {
        // 自定义动作类型没有序列化分支：必须明确失败而不是静默丢字段
        var rule = new TweakRule
        {
            Id = "test.unknown.action",
            Category = "性能",
            Risk = RiskLevel.Safe,
            DisplayName = "未知动作类型",
            Actions = new TweakAction[] { new UnknownTestAction() },
        };

        var act = () => TweakRuleWriter.Write(rule);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>测试专用未知动作类型（验证序列化端对未登记类型的拒绝行为）。</summary>
    private sealed record UnknownTestAction : TweakAction;
}
