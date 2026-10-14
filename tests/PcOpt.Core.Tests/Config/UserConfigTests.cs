// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using FluentAssertions;
using PcOpt.Core.Config;
using PcOpt.Core.Hardware;

namespace PcOpt.Core.Tests.Config;

/// <summary>
/// 用户配置测试（T10，架构 3.5）：schemaVersion 版本化、导入前校验拒绝坏包、
/// 逐项预览、gameId × hardwareTier 存储与查找回退、原子落盘往返。
/// </summary>
public sealed class UserConfigTests : IDisposable
{
    private readonly string _root;

    public UserConfigTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcopt-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private UserConfigService CreateService() => new(_root);

    private static UserConfig SampleConfig() => new()
    {
        Product = "GameModePilot",
        Profiles = new[]
        {
            new OptimizationProfile
            {
                GameId = "valorant",
                HardwareTier = HardwareTier.Ultra,
                Intensity = ProfileIntensity.Aggressive,
                EnabledCategories = new[] { "性能", "延迟" },
                DisabledRuleIds = new[] { "svc.spooler.disable" },
                ExtraRuleIds = new[] { "net.tcp.autotuning.disable" },
            },
            new OptimizationProfile
            {
                GameId = ProfileKey.GlobalGameId,
                HardwareTier = null,
                Intensity = ProfileIntensity.Balanced,
            },
        },
        Preferences = new UserPreferences
        {
            Theme = "Dark",
            ConfirmBeforeDangerRules = true,
        },
    };

    // ---------- 序列化往返 ----------

    [Fact]
    public void Serialize_ThenDeserialize_ShouldRoundTrip()
    {
        var config = SampleConfig();
        var json = UserConfigJson.Serialize(config);

        json.Should().Contain("\"schemaVersion\": 1");

        var parsed = UserConfigJson.Deserialize(json);
        parsed.IsSuccess.Should().BeTrue(parsed.Error);

        var result = parsed.Value!;
        result.SchemaVersion.Should().Be(config.SchemaVersion);
        result.Product.Should().Be(config.Product);
        result.Profiles.Should().BeEquivalentTo(config.Profiles);
        result.Preferences.Should().Be(config.Preferences);
    }

    [Fact]
    public void Serialize_OmitsNullHardwareTier_AndReadsBackAsNull()
    {
        var config = new UserConfig
        {
            Product = "GameOptBox",
            Profiles = new[] { new OptimizationProfile { GameId = "cs2", HardwareTier = null } },
        };

        var json = UserConfigJson.Serialize(config);
        json.Should().NotContain("hardwareTier");

        var parsed = UserConfigJson.Deserialize(json);
        parsed.IsSuccess.Should().BeTrue(parsed.Error);
        parsed.Value!.Profiles[0].HardwareTier.Should().BeNull();
    }

    // ---------- schema 校验拒绝坏包 ----------

    [Fact]
    public void Deserialize_MalformedJson_ShouldFail()
    {
        var result = UserConfigJson.Deserialize("{ not json");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("JSON");
    }

    [Fact]
    public void Deserialize_RootNotObject_ShouldFail()
    {
        var result = UserConfigJson.Deserialize("[1, 2, 3]");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("根节点");
    }

    [Fact]
    public void Deserialize_MissingSchemaVersion_ShouldFail()
    {
        var result = UserConfigJson.Deserialize("""{"product":"GameModePilot","profiles":[]}""");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("schemaVersion");
    }

    [Fact]
    public void Deserialize_FutureSchemaVersion_ShouldFailAndTellUserToUpgrade()
    {
        var json = """{"schemaVersion":999,"product":"GameModePilot","profiles":[]}""";
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("升级应用");
    }

    [Fact]
    public void Deserialize_UnsupportedOldSchemaVersion_ShouldFail()
    {
        var json = """{"schemaVersion":0,"product":"GameModePilot","profiles":[]}""";
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("不支持的配置架构版本");
    }

    [Fact]
    public void Deserialize_SchemaVersionWrongType_ShouldFail()
    {
        var json = """{"schemaVersion":"1","product":"GameModePilot","profiles":[]}""";
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("schemaVersion");
    }

    [Fact]
    public void Deserialize_MissingProduct_ShouldFail()
    {
        var json = """{"schemaVersion":1,"profiles":[]}""";
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("product");
    }

    [Fact]
    public void Deserialize_ProfileMissingGameId_ShouldFail()
    {
        var json = """
        {
          "schemaVersion": 1,
          "product": "GameModePilot",
          "profiles": [ { "hardwareTier": "High" } ]
        }
        """;
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("gameId");
    }

    [Fact]
    public void Deserialize_ProfileInvalidHardwareTier_ShouldFail()
    {
        var json = """
        {
          "schemaVersion": 1,
          "product": "GameModePilot",
          "profiles": [ { "gameId": "cs2", "hardwareTier": "Quantum" } ]
        }
        """;
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("hardwareTier");
    }

    [Fact]
    public void Deserialize_ProfileInvalidIntensity_ShouldFail()
    {
        var json = """
        {
          "schemaVersion": 1,
          "product": "GameModePilot",
          "profiles": [ { "gameId": "cs2", "intensity": "MaxPower" } ]
        }
        """;
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("intensity");
    }

    [Fact]
    public void Deserialize_DuplicateProfileKeys_ShouldFail()
    {
        var json = """
        {
          "schemaVersion": 1,
          "product": "GameModePilot",
          "profiles": [
            { "gameId": "cs2", "hardwareTier": "High" },
            { "gameId": "CS2", "hardwareTier": "High" }
          ]
        }
        """;
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("重复");
    }

    [Fact]
    public void Deserialize_InvalidTheme_ShouldFail()
    {
        var json = """
        {
          "schemaVersion": 1,
          "product": "GameModePilot",
          "profiles": [],
          "preferences": { "theme": "Neon" }
        }
        """;
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("theme");
    }

    [Fact]
    public void Deserialize_ProfilesNotArray_ShouldFail()
    {
        var json = """
        {
          "schemaVersion": 1,
          "product": "GameModePilot",
          "profiles": { "gameId": "cs2" }
        }
        """;
        var result = UserConfigJson.Deserialize(json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("profiles");
    }

    [Fact]
    public void Deserialize_EmptyString_ShouldFail()
    {
        var result = UserConfigJson.Deserialize("   ");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("为空");
    }

    // ---------- 逐项预览与导入合并 ----------

    [Fact]
    public void PreviewImport_NewConfig_AllItemsAreAdd()
    {
        var current = new UserConfig { Product = "GameModePilot" };
        var json = UserConfigJson.Serialize(SampleConfig());

        var preview = UserConfigJson.PreviewImport(current, json);
        preview.IsValid.Should().BeTrue(string.Join("；", preview.Errors));
        preview.Items.Should().HaveCount(2);
        preview.Items.Should().OnlyContain(i => i.Action == ConfigImportAction.Add);
        preview.Changes.Should().HaveCount(2);
    }

    [Fact]
    public void PreviewImport_SameContent_AllItemsAreUnchanged()
    {
        var current = SampleConfig();
        var json = UserConfigJson.Serialize(current);

        var preview = UserConfigJson.PreviewImport(current, json);
        preview.IsValid.Should().BeTrue(string.Join("；", preview.Errors));
        preview.Items.Should().HaveCount(2);
        preview.Items.Should().OnlyContain(i => i.Action == ConfigImportAction.Unchanged);
        preview.Changes.Should().BeEmpty();
    }

    [Fact]
    public void PreviewImport_ChangedIntensity_ItemIsUpdateWithSummary()
    {
        var current = SampleConfig();
        var incoming = current with
        {
            Profiles = new[]
            {
                current.Profiles[0] with { Intensity = ProfileIntensity.Conservative },
                current.Profiles[1],
            },
        };
        var json = UserConfigJson.Serialize(incoming);

        var preview = UserConfigJson.PreviewImport(current, json);
        preview.IsValid.Should().BeTrue(string.Join("；", preview.Errors));

        var update = preview.Items.Should().ContainSingle(i => i.Action == ConfigImportAction.Update).Subject;
        update.Summary.Should().Contain("valorant");
        update.Summary.Should().Contain("Ultra");
        preview.Changes.Should().ContainSingle();
    }

    [Fact]
    public void PreviewImport_InvalidPackage_ShouldReturnInvalidPreview()
    {
        var current = SampleConfig();
        var preview = UserConfigJson.PreviewImport(current, """{"schemaVersion":42}""");

        preview.IsValid.Should().BeFalse();
        preview.Config.Should().BeNull();
        preview.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Import_AppliesAddAndUpdate_SkipsUnchanged()
    {
        var current = SampleConfig();
        var incoming = new UserConfig
        {
            Product = "GameModePilot",
            Profiles = new[]
            {
                current.Profiles[0] with { Intensity = ProfileIntensity.Conservative }, // Update
                current.Profiles[1],                                                     // Unchanged
                new OptimizationProfile { GameId = "cs2", HardwareTier = HardwareTier.High }, // Add
            },
        };
        var json = UserConfigJson.Serialize(incoming);

        var result = UserConfigJson.Import(current, json);
        result.IsSuccess.Should().BeTrue(result.Error);

        var merged = result.Value!;
        merged.Profiles.Should().HaveCount(3);
        merged.FindProfile("valorant", HardwareTier.Ultra)!.Intensity.Should().Be(ProfileIntensity.Conservative);
        merged.FindProfile("cs2", HardwareTier.High)!.GameId.Should().Be("cs2");
    }

    [Fact]
    public void Import_DifferentProduct_ShouldFail()
    {
        var current = SampleConfig();
        var incoming = current with { Product = "FrameLab" };
        var json = UserConfigJson.Serialize(incoming);

        var result = UserConfigJson.Import(current, json);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("产品不匹配");
    }

    [Fact]
    public void Import_InvalidPackage_ShouldFailAndNotTouchCurrent()
    {
        var current = SampleConfig();
        var result = UserConfigJson.Import(current, """{"schemaVersion":1,"product":"GameModePilot","profiles":[{"gameId":""}]}""");

        result.IsSuccess.Should().BeFalse();
        current.Profiles.Should().HaveCount(2); // 原配置未被修改
    }

    // ---------- Profile 查找回退链 ----------

    [Theory]
    [InlineData("valorant", HardwareTier.Ultra, ProfileIntensity.Aggressive)]     // 精确命中
    [InlineData("valorant", HardwareTier.High, ProfileIntensity.Balanced)]        // 全局默认回退
    [InlineData("dota2", HardwareTier.Ultra, ProfileIntensity.Balanced)]          // 全局默认回退（任意游戏）
    [InlineData("unknown-game", HardwareTier.Low, ProfileIntensity.Balanced)]     // 全局默认回退（任意档位）
    public void FindProfile_ShouldFollowFallbackChain(string gameId, HardwareTier tier, ProfileIntensity expected)
    {
        var config = SampleConfig();
        var profile = config.FindProfile(gameId, tier);

        profile.Should().NotBeNull();
        profile!.Intensity.Should().Be(expected);
    }

    [Fact]
    public void FindProfile_NoGlobalFallback_ShouldReturnNull()
    {
        var config = new UserConfig
        {
            Product = "GameModePilot",
            Profiles = new[] { new OptimizationProfile { GameId = "cs2", HardwareTier = HardwareTier.High } },
        };

        config.FindProfile("dota2", HardwareTier.High).Should().BeNull();
    }

    [Fact]
    public void FindProfile_GameSpecificTierBeatsGlobalTier()
    {
        var config = new UserConfig
        {
            Product = "GameModePilot",
            Profiles = new[]
            {
                new OptimizationProfile { GameId = "*", HardwareTier = HardwareTier.Ultra, Intensity = ProfileIntensity.Conservative },
                new OptimizationProfile { GameId = "cs2", HardwareTier = HardwareTier.Ultra, Intensity = ProfileIntensity.Aggressive },
            },
        };

        config.FindProfile("cs2", HardwareTier.Ultra)!.Intensity.Should().Be(ProfileIntensity.Aggressive);
        config.FindProfile("dota2", HardwareTier.Ultra)!.Intensity.Should().Be(ProfileIntensity.Conservative);
    }

    // ---------- 服务层：文件读写与产品名消毒 ----------

    [Fact]
    public void Load_MissingFile_ShouldReturnDefaultConfig()
    {
        var result = CreateService().Load("GameModePilot");
        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Product.Should().Be("GameModePilot");
        result.Value.Profiles.Should().BeEmpty();
        result.Value.SchemaVersion.Should().Be(UserConfigSchema.CurrentVersion);
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTrip()
    {
        var service = CreateService();
        var save = service.Save(SampleConfig());
        save.IsSuccess.Should().BeTrue(save.Error);

        var load = service.Load("GameModePilot");
        load.IsSuccess.Should().BeTrue(load.Error);
        load.Value!.Profiles.Should().BeEquivalentTo(SampleConfig().Profiles);
        load.Value.Preferences.Should().Be(SampleConfig().Preferences);
    }

    [Fact]
    public void Load_CorruptFile_ShouldFailWithClearMessage()
    {
        var service = CreateService();
        var path = service.GetConfigFilePath("GameModePilot");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ corrupted");

        var result = service.Load("GameModePilot");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("损坏");
    }

    [Fact]
    public void Load_FutureVersionFile_ShouldFail()
    {
        var service = CreateService();
        var path = service.GetConfigFilePath("GameModePilot");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"schemaVersion":99,"product":"GameModePilot"}""");

        var result = service.Load("GameModePilot");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("升级应用");
    }

    [Fact]
    public void Save_NormalizesOldSchemaVersion()
    {
        var service = CreateService();
        var stale = new UserConfig { Product = "GameModePilot", SchemaVersion = 0 };

        service.Save(stale).IsSuccess.Should().BeTrue();

        var path = service.GetConfigFilePath("GameModePilot");
        File.ReadAllText(path).Should().Contain("\"schemaVersion\": 1");
    }

    [Fact]
    public void Save_ShouldNotLeaveTempFile()
    {
        var service = CreateService();
        service.Save(SampleConfig()).IsSuccess.Should().BeTrue();

        var path = service.GetConfigFilePath("GameModePilot");
        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void GetConfigFilePath_PathTraversalProductName_ShouldThrow()
    {
        var service = CreateService();
        var act = () => service.GetConfigFilePath("../../etc");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetConfigFilePath_ValidProductName_ShouldBeUnderAppDataRoot()
    {
        var service = CreateService();
        var path = service.GetConfigFilePath("GameOptBox");

        path.Should().Be(Path.Combine(_root, "GameOptBox", "config.json"));
    }

    [Fact]
    public void ServiceImport_MergesIntoCurrent()
    {
        var service = CreateService();
        service.Save(SampleConfig()).IsSuccess.Should().BeTrue();

        var incoming = SampleConfig() with
        {
            Profiles = new[]
            {
                SampleConfig().Profiles[1],
                new OptimizationProfile { GameId = "eldenring", HardwareTier = HardwareTier.Mid },
            },
        };
        var result = service.Import(SampleConfig(), UserConfigJson.Serialize(incoming));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Profiles.Should().HaveCount(3);
    }
}
