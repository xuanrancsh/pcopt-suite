// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Core.Compliance;

/// <summary>
/// 合规红线守卫（PRD 第 6 章红线 #1：不做显卡型号伪装，GPU 类注册表键只读）。
/// 所有触碰注册表写入的路径必须经 <see cref="EnsureWritable"/> 检查；
/// CI / 单元测试对显卡类键有写入拒绝断言（架构 8.8）。
/// </summary>
public static class ComplianceGuard
{
    /// <summary>显卡（显示适配器）设备类 GUID。</summary>
    public const string DisplayAdapterClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>只读注册表路径前缀（小写、反斜杠规范形式）：PCI 枚举键与显卡设备类键。</summary>
    private static readonly string[] ReadOnlyPathPrefixes =
    {
        @"hklm\system\currentcontrolset\enum\pci",
        @"hklm\system\currentcontrolset\control\class\" + DisplayAdapterClassGuid,
    };

    /// <summary>
    /// 校验注册表路径可写。
    /// 命中 GPU / PCI 设备类只读前缀时返回失败（明确中文原因，供 UI 展示）。
    /// </summary>
    public static Result EnsureWritable(string? registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
        {
            return Result.Fail("注册表路径为空");
        }

        var normalized = NormalizePath(registryPath);
        foreach (var prefix in ReadOnlyPathPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Result.Fail("合规限制：显卡 / PCI 设备类注册表键为只读（硬件信息只真实展示，PRD 红线 #1）");
            }
        }

        return Result.Ok();
    }

    /// <summary>路径规范化：统一反斜杠、统一根键简写、转小写，便于前缀比较。</summary>
    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('/', '\\').ToLowerInvariant();
        foreach (var (alias, canonical) in new[]
        {
            ("hkey_local_machine\\", "hklm\\"),
            ("hkey_current_user\\", "hkcu\\"),
            ("hkey_classes_root\\", "hkcr\\"),
            ("hkey_users\\", "hku\\"),
            ("hkey_current_config\\", "hkcc\\"),
        })
        {
            if (normalized.StartsWith(alias, StringComparison.Ordinal))
            {
                normalized = canonical + normalized[alias.Length..];
                break;
            }
        }

        return normalized;
    }
}
