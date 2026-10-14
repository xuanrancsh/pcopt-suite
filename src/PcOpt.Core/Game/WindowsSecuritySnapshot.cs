// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Win32;

namespace PcOpt.Core.Game;

/// <summary>
/// 系统安全态势快照（T9 反作弊检测的输入，只读）。
/// 抽象使检测逻辑可在单测中注入伪造快照，不依赖真实机器样本。
/// </summary>
public interface ISystemSecuritySnapshot
{
    /// <summary>已安装服务 / 驱动名（注册表 Services 子键名，大小写不敏感比较）。</summary>
    IReadOnlyList<string> ServiceNames { get; }

    /// <summary>驱动文件名（System32\drivers 目录及服务 ImagePath 中的文件名）。</summary>
    IReadOnlyList<string> DriverFileNames { get; }

    /// <summary>运行中进程名（可执行文件名）。</summary>
    IReadOnlyList<string> ProcessNames { get; }
}

/// <summary>
/// Windows 真实安全态势快照（T9，全部只读）：
/// <list type="bullet">
/// <item>服务名：注册表 <c>HKLM\SYSTEM\CurrentControlSet\Services</c> 子键（含驱动服务）；</item>
/// <item>驱动文件：服务 ImagePath 中的文件名 + <c>System32\drivers</c> 目录列表；</item>
/// <item>进程名：当前运行进程可执行文件名。</item>
/// </list>
/// 合规声明：不打开目标进程、不读写内存（红线 #4）。
/// </summary>
public sealed class WindowsSecuritySnapshot : ISystemSecuritySnapshot
{
    private const string ServicesKeyPath = @"SYSTEM\CurrentControlSet\Services";

    /// <inheritdoc/>
    public IReadOnlyList<string> ServiceNames
    {
        get
        {
            var names = new List<string>();
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(ServicesKeyPath);
                if (key is not null)
                {
                    names.AddRange(key.GetSubKeyNames());
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
            {
                // 注册表不可读：按"无服务"处理（宁可漏报，不可误报）
            }

            return names;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> DriverFileNames
    {
        get
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 服务 ImagePath 中的文件名（覆盖不在 System32\drivers 的驱动）
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(ServicesKeyPath);
                if (key is not null)
                {
                    foreach (var serviceName in key.GetSubKeyNames())
                    {
                        using var serviceKey = key.OpenSubKey(serviceName);
                        var imagePath = serviceKey?.GetValue("ImagePath") as string;
                        var fileName = ExtractFileName(imagePath);
                        if (fileName is not null)
                        {
                            files.Add(fileName);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.SecurityException or UnauthorizedAccessException)
            {
                // 忽略：降级到目录枚举
            }

            // 2) System32\drivers 目录枚举
            try
            {
                var driversDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                if (Directory.Exists(driversDirectory))
                {
                    foreach (var file in Directory.GetFiles(driversDirectory))
                    {
                        files.Add(Path.GetFileName(file));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 忽略
            }

            return files.ToArray();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ProcessNames
    {
        get
        {
            try
            {
                return System.Diagnostics.Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            return p.ProcessName;
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                        {
                            return null;
                        }
                    })
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>从 ImagePath 提取文件名（兼容 NT 路径与带引号路径）。</summary>
    internal static string? ExtractFileName(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var text = imagePath.Trim().Trim('"');
        if (text.Length == 0)
        {
            return null;
        }

        // NT 路径（\SystemRoot\... / \??\...）：无法映射为用户路径时仍可取末段文件名
        var separatorIndex = text.LastIndexOfAny(new[] { '\\', '/' });
        var fileName = separatorIndex >= 0 ? text[(separatorIndex + 1)..] : text;
        return fileName.Length == 0 ? null : fileName;
    }
}

/// <summary>反作弊检测器接口（T9）。</summary>
public interface IAntiCheatDetector
{
    /// <summary>检测当前系统是否运行已知反作弊。</summary>
    AntiCheatDetectionResult Detect();
}

/// <summary>
/// 反作弊检测器（T9，架构 3.4）。
/// 规则：
/// <list type="bullet">
/// <item>任一产品的内核特征命中 → <see cref="AntiCheatProfile.Kernel"/>（IFEO 降级信号）；</item>
/// <item>仅用户态特征命中 → <see cref="AntiCheatProfile.UserMode"/>；</item>
/// <item>零误操作原则：无任何命中 → <see cref="AntiCheatProfile.None"/>（识别不到默认 None，绝不猜测）；</item>
/// <item>多产品同时命中时取最高威胁等级（Kernel 优先），产品取第一个内核命中者。</item>
/// </list>
/// </summary>
public sealed class AntiCheatDetector : IAntiCheatDetector
{
    private readonly ISystemSecuritySnapshot _snapshot;

    /// <summary>初始化检测器。</summary>
    /// <param name="snapshot">系统安全态势快照（可注入替身）。</param>
    public AntiCheatDetector(ISystemSecuritySnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    /// <inheritdoc/>
    public AntiCheatDetectionResult Detect()
    {
        AntiCheatSignature? userModeHit = null;
        var userModeFeatures = new List<AntiCheatFeature>();

        foreach (var signature in AntiCheatSignatureLibrary.Signatures)
        {
            var kernelMatches = MatchFeatures(signature.KernelFeatures);
            if (kernelMatches.Count > 0)
            {
                return new AntiCheatDetectionResult(AntiCheatProfile.Kernel, signature.Product, kernelMatches);
            }

            if (userModeHit is null)
            {
                var userMatches = MatchFeatures(signature.UserModeFeatures);
                if (userMatches.Count > 0)
                {
                    userModeHit = signature;
                    userModeFeatures = userMatches;
                }
            }
        }

        return userModeHit is null
            ? AntiCheatDetectionResult.None
            : new AntiCheatDetectionResult(AntiCheatProfile.UserMode, userModeHit.Product, userModeFeatures);
    }

    private List<AntiCheatFeature> MatchFeatures(IReadOnlyList<AntiCheatFeature> features)
    {
        var matches = new List<AntiCheatFeature>();
        foreach (var feature in features)
        {
            var pool = feature.Kind switch
            {
                AntiCheatFeatureKind.DriverFile => _snapshot.DriverFileNames,
                AntiCheatFeatureKind.ServiceName => _snapshot.ServiceNames,
                AntiCheatFeatureKind.ProcessName => _snapshot.ProcessNames,
                _ => Array.Empty<string>(),
            };

            foreach (var candidate in pool)
            {
                if (string.Equals(candidate, feature.Value, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(feature);
                    break;
                }
            }
        }

        return matches;
    }
}
