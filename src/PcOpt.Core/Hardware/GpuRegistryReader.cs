// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Win32;
using PcOpt.Core.Compliance;

namespace PcOpt.Core.Hardware;

/// <summary>
/// 显卡信息注册表读取器（架构 3.3：注册表显卡类键**只读**解析，合规红线 #1）。
/// 读取位置：<c>HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000N</c>：
/// <list type="bullet">
/// <item><c>HardwareInformation.AdapterString</c> → 型号；</item>
/// <item><c>HardwareInformation.qwMemorySize</c> → 显存（REG_QWORD；旧驱动为 8 字节 REG_BINARY）；</item>
/// <item><c>DriverVersion</c> → 驱动版本。</item>
/// </list>
/// 本类仅调用只读 API（OpenSubKey 只读 / GetValue）；写入路径被
/// <see cref="ComplianceGuard"/> 黑名单拦截（有自动化测试断言，PRD 红线 #1）。
/// </summary>
public sealed class GpuRegistryReader : IGpuInfoReader
{
    /// <summary>显示适配器设备类 GUID。</summary>
    public const string DisplayAdapterClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>显卡类注册表路径（HKLM，只读）。</summary>
    public const string DisplayAdapterClassPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\" + DisplayAdapterClassGuid;

    /// <summary>最大枚举子键序号（0000..0031，足够覆盖常见多显卡平台）。</summary>
    private const int MaxAdapterIndex = 32;

    /// <summary>读取全部显示适配器信息（只读）。任何注册表异常按"无数据"处理。</summary>
    public IReadOnlyList<GpuInfo> ReadDisplayAdapters()    {
        // 合规说明（红线 #1）：本方法仅调用只读 API（OpenSubKey / GetValue），
        // 不触碰任何写入 API；显卡类键的"只读"由 ComplianceGuard 在所有写入路径上强制
        // （RegistrySetActionHandler 的 Apply/Revert 均前置 EnsureWritable 拦截），
        // 并由单元测试对写路径断言拒绝。此处读取无需守卫放行。
        var adapters = new List<GpuInfo>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var classKey = baseKey.OpenSubKey(ToRelativePath(DisplayAdapterClassPath));
            if (classKey is null)
            {
                return Array.Empty<GpuInfo>();
            }

            for (var index = 0; index < MaxAdapterIndex; index++)
            {
                using var adapterKey = classKey.OpenSubKey(index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
                if (adapterKey is null)
                {
                    continue;
                }

                var name = adapterKey.GetValue("HardwareInformation.AdapterString")?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    // 无型号字符串的条目（如幽灵 / 已卸载设备）跳过，不猜测
                    continue;
                }

                adapters.Add(new GpuInfo(
                    InferVendor(name),
                    name.Trim(),
                    ReadVideoMemoryBytes(adapterKey),
                    adapterKey.GetValue("DriverVersion")?.ToString()?.Trim()));
            }
        }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            // 注册表读取被拒 / IO 错误：按"无数据"处理（不抛，UI 可重试）
            return Array.Empty<GpuInfo>();
        }

        return adapters;
    }

    /// <inheritdoc/>
    public IReadOnlyList<GpuInfo> ReadGpus() => ReadDisplayAdapters();

    /// <summary>
    /// 从适配器型号字符串推断厂商（纯函数，便于单测）。
    /// 无法推断时返回 <see cref="GpuVendor.Unknown"/>——宁可未知，绝不猜测（合规红线 #1）。
    /// </summary>
    public static GpuVendor InferVendor(string? adapterString)
    {
        if (string.IsNullOrWhiteSpace(adapterString))
        {
            return GpuVendor.Unknown;
        }

        var text = adapterString.Trim();
        if (text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || text.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Quadro", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Nvidia;
        }

        if (text.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ATI", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Amd;
        }

        if (text.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("UHD", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Iris", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Arc", StringComparison.OrdinalIgnoreCase))
        {
            return GpuVendor.Intel;
        }

        return GpuVendor.Unknown;
    }

    /// <summary>解析显存字节数：REG_QWORD（long）或 8 字节 REG_BINARY；无值或为 0 返回 null（不猜测）。</summary>
    internal static long? ReadVideoMemoryBytes(RegistryKey adapterKey)
    {
        var raw = adapterKey.GetValue("HardwareInformation.qwMemorySize");
        switch (raw)
        {
            case long qword when qword > 0:
                return qword;
            case int dword when dword > 0:
                return dword;
            case byte[] binary when binary.Length >= 8:
                var value = BitConverter.ToInt64(binary, 0);
                return value > 0 ? value : null;
            default:
                return null;
        }
    }

    private static string ToRelativePath(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');
        var separatorIndex = normalized.IndexOf('\\');
        return separatorIndex < 0 ? string.Empty : normalized[(separatorIndex + 1)..];
    }
}
