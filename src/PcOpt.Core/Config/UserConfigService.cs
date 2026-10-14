// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Linq;
using System.Text;

namespace PcOpt.Core.Config;

/// <summary>
/// 用户配置服务（架构 3.5：%AppData%\pcopt\{product}\config.json）。
/// 职责：路径解析（产品名消毒防路径穿越）、原子写、加载（缺失 → 默认配置）、导入编排。
/// schema 校验与逐项预览在 <see cref="UserConfigJson"/>，本服务只做文件编排，便于单测注入根目录。
/// </summary>
public sealed class UserConfigService
{
    /// <summary>产品名白名单字符（防止路径穿越：仅字母、数字、-、_）。</summary>
    private static readonly char[] ProductNameAllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_".ToCharArray();

    private readonly string _appDataRoot;

    /// <summary>
    /// 初始化服务。
    /// </summary>
    /// <param name="appDataRoot">配置根目录；null 时取 %AppData%\pcopt（单测注入临时目录）。</param>
    public UserConfigService(string? appDataRoot = null)
    {
        _appDataRoot = string.IsNullOrWhiteSpace(appDataRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "pcopt")
            : appDataRoot;
    }

    /// <summary>配置根目录。</summary>
    public string AppDataRoot => _appDataRoot;

    /// <summary>某产品 config.json 的完整路径。</summary>
    public string GetConfigFilePath(string product)
    {
        var sanitized = SanitizeProduct(product);
        return Path.Combine(_appDataRoot, sanitized, "config.json");
    }

    /// <summary>
    /// 加载配置：文件缺失 → 返回默认配置（Ok）；文件存在但损坏 / schema 非法 → Fail（人话错误）。
    /// </summary>
    public Result<UserConfig> Load(string product)
    {
        var sanitized = SanitizeProduct(product);
        var path = GetConfigFilePath(sanitized);
        if (!File.Exists(path))
        {
            return Result<UserConfig>.Ok(new UserConfig { Product = sanitized });
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<UserConfig>.Fail($"读取配置文件失败：{ex.Message}");
        }

        var parsed = UserConfigJson.Deserialize(json);
        if (!parsed.IsSuccess)
        {
            return Result<UserConfig>.Fail($"配置文件 {path} 已损坏或版本不兼容：{parsed.Error}");
        }

        return Result<UserConfig>.Ok(parsed.Value!);
    }

    /// <summary>
    /// 保存配置（先校验再原子落盘：写临时文件 → 整体替换，避免半截文件）。
    /// 落盘前强制 schemaVersion 为当前版本（防御旧版本对象被直接保存）。
    /// </summary>
    public Result Save(UserConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var sanitized = SanitizeProduct(config.Product);
        var normalized = config.SchemaVersion == UserConfigSchema.CurrentVersion
            ? config
            : config with { SchemaVersion = UserConfigSchema.CurrentVersion };

        var path = GetConfigFilePath(sanitized);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = UserConfigJson.Serialize(normalized);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"保存配置文件失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 导入配置文件内容：schema 校验 + 串产品拦截 + 逐项合并。
    /// 返回合并后的新配置（调用方负责 <see cref="Save"/> 落盘，便于"预览 → 用户确认 → 保存"流程）。
    /// </summary>
    public Result<UserConfig> Import(UserConfig current, string json)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<UserConfig>.Fail("导入内容为空");
        }

        return UserConfigJson.Import(current, json);
    }

    /// <summary>消毒产品名：仅允许白名单字符（防路径穿越，覆盖 Unicode 输入）。</summary>
    private static string SanitizeProduct(string? product)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            throw new ArgumentException("产品名不能为空", nameof(product));
        }

        var trimmed = product.Trim();
        if (trimmed.Length > 64 || trimmed.Any(c => !ProductNameAllowedChars.Contains(c)))
        {
            throw new ArgumentException(
                $"产品名包含非法字符或过长：{trimmed}（仅允许字母、数字、-、_，且不超过 64 字符）",
                nameof(product));
        }

        return trimmed;
    }
}
