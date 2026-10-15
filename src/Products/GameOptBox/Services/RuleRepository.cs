// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Tweak;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>规则包加载报告（T21 / B-P0-2）。</summary>
/// <param name="Loaded">成功验签并注册的规则数。</param>
/// <param name="Failed">加载失败（验签不过 / 解析失败 / 违反官方包策略 / 缺签名）的规则数。</param>
/// <param name="Failures">失败明细（含文件名与人话原因）。</param>
/// <param name="PublicKeyAvailable">公钥是否可用（false 时任何签名包都无法加载）。</param>
public sealed record RulePackageLoadReport(int Loaded, int Failed, IReadOnlyList<string> Failures, bool PublicKeyAvailable);

/// <summary>全部还原报告。</summary>
/// <param name="RevertedRules">成功还原的规则数。</param>
/// <param name="RevertedEntries">成功还原的日志条目数。</param>
/// <param name="FullyReverted">是否全部成功。</param>
public sealed record RevertAllReport(int RevertedRules, int RevertedEntries, bool FullyReverted);

/// <summary>
/// 规则仓库（T21，架构 4.2 / PRD B-P0-2）：
/// 启动时从 <c>rules\tweaks</c> 加载全部 <c>*.yaml</c>，逐条走签名验证（<c>.sig</c>）+ 官方包内容策略，
/// 通过后注册进 <see cref="TweakEngine"/>；提供搜索 / 状态查询 / 应用 / 还原。
/// 未签名或签名不符一律拒绝（架构 3.5）。
/// </summary>
public sealed class RuleRepository
{
    private readonly TweakEngine _engine;
    private readonly string _tweaksDirectory;
    private readonly bool _publicKeyAvailable;
    private readonly ILogger<RuleRepository> _logger;
    private readonly List<TweakRule> _qualified = new();

    /// <summary>初始化规则仓库。</summary>
    /// <param name="engine">已配置公钥的内核规则引擎。</param>
    /// <param name="tweaksDirectory">签名规则目录（rules\tweaks）。</param>
    /// <param name="publicKeyPath">公钥文件路径（仅用于诊断；实际验签由引擎完成）。</param>
    /// <param name="logger">日志。</param>
    public RuleRepository(
        TweakEngine engine,
        string tweaksDirectory,
        string? publicKeyPath,
        ILogger<RuleRepository> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _tweaksDirectory = tweaksDirectory ?? string.Empty;
        _publicKeyAvailable = !string.IsNullOrWhiteSpace(publicKeyPath) && File.Exists(publicKeyPath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>产品名（Journal 归属）。</summary>
    public static string ProductName => ResourcePaths.ProductName;

    /// <summary>规则已应用状态发生变化时触发（供导航徽标等刷新）。</summary>
    public event EventHandler? AppliedStateChanged;

    /// <summary>已加载的官方规则（按分类、Id 排序）。</summary>
    public IReadOnlyList<TweakRule> Rules => _qualified;

    /// <summary>规则总数。</summary>
    public int Count => _qualified.Count;

    /// <summary>
    /// 加载签名规则包：对目录下每个 <c>*.yaml</c> 读取原始字节与其同名 <c>.sig</c>，
    /// 经 <see cref="TweakEngine.LoadRulePackage"/> 验签 + 策略校验后注册。
    /// 单条失败不影响其余规则；全部失败也返回成功报告（UI 依据 Failures 提示）。
    /// </summary>
    public Result<RulePackageLoadReport> LoadPackage()
    {
        _qualified.Clear();
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(_tweaksDirectory) || !Directory.Exists(_tweaksDirectory))
        {
            return Result<RulePackageLoadReport>.Fail($"未找到随包的规则目录（{ResourcePaths.TweaksRelativeDirectory}）");
        }

        var yamlFiles = Directory.EnumerateFiles(_tweaksDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        var loaded = 0;
        foreach (var yamlPath in yamlFiles)
        {
            var fileName = Path.GetFileName(yamlPath);
            byte[] content;
            try
            {
                content = File.ReadAllBytes(yamlPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{fileName}：读取失败（{ex.Message}）");
                continue;
            }

            var sigPath = Path.ChangeExtension(yamlPath, ".sig");
            byte[]? signature = null;
            if (File.Exists(sigPath))
            {
                try
                {
                    var sigText = File.ReadAllText(sigPath).Trim();
                    signature = Convert.FromBase64String(sigText);
                }
                catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{fileName}：签名文件无效（{ex.Message}）");
                    continue;
                }
            }

            var result = _engine.LoadRulePackage(content, signature);
            if (!result.IsSuccess)
            {
                failures.Add($"{fileName}：{result.Error}");
                continue;
            }

            _qualified.Add(result.Value!);
            loaded++;
        }

        _qualified.Sort(static (a, b) =>
        {
            var byCategory = string.Compare(a.Category, b.Category, StringComparison.CurrentCulture);
            return byCategory != 0 ? byCategory : string.CompareOrdinal(a.Id, b.Id);
        });

        _logger.LogInformation("规则包加载：成功 {Ok}，失败 {Fail}（目录 {Dir}）", loaded, failures.Count, _tweaksDirectory);
        return Result<RulePackageLoadReport>.Ok(
            new RulePackageLoadReport(loaded, failures.Count, failures, _publicKeyAvailable));
    }

    /// <summary>搜索 / 过滤规则。</summary>
    /// <param name="query">关键词。</param>
    /// <param name="category">分类。</param>
    /// <param name="includeSensitive">是否包含危险（敏感）项。</param>
    public IReadOnlyList<TweakRule> Search(string? query, string? category, bool includeSensitive) =>
        RuleSearch.Filter(_qualified, query, category, includeSensitive);

    /// <summary>分类去重列表。</summary>
    public IReadOnlyList<string> Categories => RuleSearch.DistinctCategories(_qualified);

    /// <summary>规则当前是否已应用（以 Journal 未还原条目为准）。</summary>
    public bool IsApplied(string ruleId) => _engine.IsApplied(ruleId, ProductName);

    /// <summary>当前已应用的规则数（"已生效 N 项"徽标）。</summary>
    public int CountApplied() => _engine.LoadedRules.Count(rule => IsApplied(rule.Id));

    /// <summary>引擎中全部已加载规则（含官方签名包与程序内动态规则）。</summary>
    public IReadOnlyList<TweakRule> AllLoadedRules => _engine.LoadedRules.ToList();

    /// <summary>注册程序内动态规则（如"游戏模式服务方案"）；跳过 YAML 解析与签名校验。</summary>
    public Result RegisterRule(TweakRule rule) =>
        _engine.RegisterRule(rule).IsSuccess
            ? Result.Ok()
            : Result.Fail($"注册规则“{rule.Id}”失败");

    /// <summary>应用一条规则（幂等；失败返回人话错误）。</summary>
    public Result<RuleApplyReport> Apply(string ruleId)
    {
        var result = _engine.ApplyRule(ruleId, ProductName);
        if (result.IsSuccess)
        {
            AppliedStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    /// <summary>还原一条规则。</summary>
    public Result<RuleRevertReport> Revert(string ruleId)
    {
        var result = _engine.RevertRule(ruleId, ProductName);
        if (result.IsSuccess)
        {
            AppliedStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    /// <summary>还原全部已应用规则（含动态规则）。</summary>
    public Result<RevertAllReport> RevertAll()
    {
        // 关键路径：先严格读取未还原条目所属规则，读失败显式失败——绝不因读不到而漏还原并误报成功。
        var openResult = _engine.ListOpenRuleIds(ProductName);
        if (!openResult.IsSuccess)
        {
            return Result<RevertAllReport>.Fail($"无法读取还原日志，已终止全部还原：{openResult.Error}");
        }

        var openIds = openResult.Value!;
        var applied = _engine.LoadedRules
            .Where(rule => openIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var revertedRules = 0;
        var revertedEntries = 0;
        foreach (var rule in applied)
        {
            var result = _engine.RevertRule(rule.Id, ProductName);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("还原规则 {RuleId} 失败：{Error}", rule.Id, result.Error);
                AppliedStateChanged?.Invoke(this, EventArgs.Empty);
                return Result<RevertAllReport>.Fail($"还原“{rule.DisplayName}”失败：{result.Error}");
            }

            revertedRules++;
            revertedEntries += result.Value!.EntriesReverted;
        }

        AppliedStateChanged?.Invoke(this, EventArgs.Empty);
        return Result<RevertAllReport>.Ok(new RevertAllReport(revertedRules, revertedEntries, true));
    }
}
