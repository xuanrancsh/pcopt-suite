// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Core.Journal;
using PcOpt.Core.Tweak;
using PcOpt.Products.GameOptBox.Services;

namespace PcOpt.Products.GameOptBox.Tests;

/// <summary>
/// 首发规则包自证测试（T20-T29 交付硬性要求，PRD B-P0-2）：
/// <list type="bullet">
/// <item>规则数 ≥ 60；</item>
/// <item>每条规则都存在同名 <c>.sig</c> 且能通过 <see cref="RuleSignatureVerifier.Verify"/> 验签；</item>
/// <item>每条规则都能通过 <see cref="TweakEngine.LoadRulePackage"/>（验签 + 官方包内容策略）；</item>
/// <item>每条规则都满足 <see cref="OfficialRulePolicy"/>（可自动还原，合规红线 #9）；</item>
/// <item>每个动作都有还原实现（<see cref="OfficialRulePolicy.IsAutoRevertible"/>）。</item>
/// </list>
/// </summary>
public sealed class OfficialRulePackageTests
{
    private static readonly string[] RequiredCategories = { "性能", "游戏", "延迟", "网络", "隐私", "体验" };

    [Fact]
    public void AllOfficialRules_ShouldVerifyAndPassPolicy()
    {
        var tweaksDirectory = RepoLayout.TweaksDirectory;
        Directory.Exists(tweaksDirectory).Should().BeTrue($"应存在随包规则目录：{tweaksDirectory}");

        var verifierResult = RuleSignatureVerifier.CreateFromFile(RepoLayout.PublicKeyPath);
        verifierResult.IsSuccess.Should().BeTrue(verifierResult.Error);

        using var temp = new TempDirectory();
        var journalStore = new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal"));
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            journalStore,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance,
            verifierResult.Value);

        var yamlFiles = Directory.GetFiles(tweaksDirectory, "*.yaml", SearchOption.TopDirectoryOnly);
        yamlFiles.Length.Should().BeGreaterThanOrEqualTo(60, "首发规则包应 ≥ 60 项（PRD B-P0-2）");

        foreach (var yamlPath in yamlFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(yamlPath);
            var sigPath = Path.ChangeExtension(yamlPath, ".sig");

            File.Exists(sigPath).Should().BeTrue($"规则 {fileName} 缺少同名签名文件 .sig");

            var content = File.ReadAllBytes(yamlPath);
            var signature = Convert.FromBase64String(File.ReadAllText(sigPath).Trim());
            signature.Length.Should().Be(RuleSignatureVerifier.SignatureSize, $"规则 {fileName} 的签名长度应为 64 字节");

            // 1) 真实签名校验
            verifierResult.Value!.Verify(content, signature).IsSuccess.Should().BeTrue($"规则 {fileName} 验签应通过");

            // 2) 真实加载路径（引擎内部再次验签 + 官方包内容策略）
            var load = engine.LoadRulePackage(content, signature);
            load.IsSuccess.Should().BeTrue($"规则 {fileName} 应能通过 LoadRulePackage：{load.Error}");
            var rule = load.Value!;

            // 3) 官方包内容策略（合规红线 #9）
            OfficialRulePolicy.ValidateRule(rule).IsSuccess.Should().BeTrue($"规则 {fileName} 不应包含不可自动还原的动作");

            // 4) 每个动作都有还原实现
            rule.Actions.Should().NotBeEmpty($"规则 {fileName} 必须至少有一个动作");
            rule.Actions.Should().OnlyContain(action => OfficialRulePolicy.IsAutoRevertible(action),
                $"规则 {fileName} 的每个动作都必须可自动还原（即存在还原定义）");

            // 5) 命名与元数据规范（架构 8.1）
            rule.Id.Split('.').Length.Should().BeGreaterThanOrEqualTo(3, $"规则 {fileName} 的 Id 应为 分类.对象.动作");
            RequiredCategories.Should().Contain(rule.Category, $"规则 {fileName} 的分类应在 6 大类内");
            rule.DisplayName.Should().NotBeNullOrWhiteSpace($"规则 {fileName} 必须有人话名");
            rule.ConsequenceHints.Length.Should().BeLessThanOrEqualTo(3, $"规则 {fileName} 的后果提示应 ≤ 3 条（PRD 5.3）");
        }
    }

    [Fact]
    public void OfficialRulePackage_ShouldCoverAllSixCategories()
    {
        var tweaksDirectory = RepoLayout.TweaksDirectory;
        var verifierResult = RuleSignatureVerifier.CreateFromFile(RepoLayout.PublicKeyPath);
        using var temp = new TempDirectory();
        var journalStore = new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal"));
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            journalStore,
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance,
            verifierResult.Value);

        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var yamlPath in Directory.GetFiles(tweaksDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var content = File.ReadAllBytes(yamlPath);
            var signature = Convert.FromBase64String(File.ReadAllText(Path.ChangeExtension(yamlPath, ".sig")).Trim());
            var load = engine.LoadRulePackage(content, signature);
            load.IsSuccess.Should().BeTrue(load.Error);
            categories.Add(load.Value!.Category);
        }

        foreach (var category in RequiredCategories)
        {
            categories.Should().Contain(category, $"首发规则包应覆盖“{category}”分类");
        }
    }

    [Fact]
    public void TamperedRuleContent_ShouldBeRejected()
    {
        var tweaksDirectory = RepoLayout.TweaksDirectory;
        var yamlPath = Directory.GetFiles(tweaksDirectory, "*.yaml", SearchOption.TopDirectoryOnly)[0];
        var content = File.ReadAllBytes(yamlPath);
        var signature = Convert.FromBase64String(File.ReadAllText(Path.ChangeExtension(yamlPath, ".sig")).Trim());

        // 篡改内容后仍用原签名：必须被拒绝（架构 3.5：签名与内容不符拒绝执行）
        var tampered = (byte[])content.Clone();
        tampered[tampered.Length - 2] ^= 0x01;

        var verifierResult = RuleSignatureVerifier.CreateFromFile(RepoLayout.PublicKeyPath);
        verifierResult.Value!.Verify(tampered, signature).IsSuccess.Should().BeFalse("篡改内容后验签必须失败");

        using var temp = new TempDirectory();
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal")),
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance,
            verifierResult.Value);
        engine.LoadRulePackage(tampered, signature).IsSuccess.Should().BeFalse("篡改内容不应被引擎加载");
    }

    [Fact]
    public void RuleRepository_ShouldLoadAllRulesThroughSignedPath()
    {
        using var temp = new TempDirectory();
        var engine = new TweakEngine(
            Array.Empty<ITweakActionHandler>(),
            new JournalStore(NullLogger<JournalStore>.Instance, temp.Combine("journal")),
            new TestSystemEnvironment(),
            NullLogger<TweakEngine>.Instance,
            RuleSignatureVerifier.CreateFromFile(RepoLayout.PublicKeyPath).Value);

        var repository = new RuleRepository(engine, RepoLayout.TweaksDirectory, RepoLayout.PublicKeyPath, NullLogger<RuleRepository>.Instance);
        var report = repository.LoadPackage();

        report.IsSuccess.Should().BeTrue(report.Error);
        report.Value!.PublicKeyAvailable.Should().BeTrue();
        report.Value.Failed.Should().Be(0, $"不应有加载失败的规则：{string.Join("；", report.Value.Failures)}");
        report.Value.Loaded.Should().BeGreaterThanOrEqualTo(60);
        repository.Rules.Should().HaveCount(report.Value.Loaded);
    }
}
