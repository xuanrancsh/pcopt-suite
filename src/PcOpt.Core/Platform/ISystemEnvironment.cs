// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Runtime.InteropServices;

namespace PcOpt.Core.Platform;

/// <summary>
/// CPU 架构（与 <see cref="Architecture"/> 一一对应，供 YAML 条件匹配使用）。
/// </summary>
public enum CpuArchitecture
{
    /// <summary>未知 / 未能识别。</summary>
    Unknown = 0,

    /// <summary>x86（32 位）。</summary>
    X86,

    /// <summary>x64（64 位）。</summary>
    X64,

    /// <summary>ARM64。</summary>
    Arm64,
}

/// <summary>
/// 操作系统版本信息（不可变记录）。
/// </summary>
/// <param name="Name">操作系统名称，如 "Windows"。</param>
/// <param name="Version">版本号，如 10.0.22631（注意 Win11 的 Major/Minor 仍为 10.0，需以 Build 区分）。</param>
/// <param name="Build">Build 号，如 22631（Win11 23H2）、19045（Win10 22H2）、26100（Win11 24H2）。</param>
public sealed record OsVersionInfo(string Name, Version Version, int Build);

/// <summary>
/// 系统环境只读抽象：OS 版本 / CPU 架构 / GPU 厂商。
/// TweakEngine 的 <c>OsCondition</c> 匹配依赖本接口，测试可注入替身（不触碰真实系统）。
/// </summary>
public interface ISystemEnvironment
{
    /// <summary>操作系统版本信息。</summary>
    OsVersionInfo OsVersion { get; }

    /// <summary>CPU 架构。</summary>
    CpuArchitecture CpuArchitecture { get; }

    /// <summary>GPU 厂商（"NVIDIA" / "AMD" / "Intel"）；未知时返回空字符串。</summary>
    string GpuVendor { get; }
}

/// <summary>
/// Windows 默认实现。
/// OS 版本取自 <see cref="Environment.OSVersion"/>（.NET 5+ 在 Windows 上返回真实版本，不受兼容性清单影响），
/// CPU 架构取自 <see cref="RuntimeInformation.OSArchitecture"/>。
/// GPU 厂商检测在 T6（HardwareDetect）接入前返回空（未知），
/// 因此带 GPU 厂商条件的规则在接入前不会被匹配（保守策略，见 <c>OsConditionMatcher</c>）。
/// </summary>
public sealed class WindowsSystemEnvironment : ISystemEnvironment
{
    /// <inheritdoc/>
    public OsVersionInfo OsVersion { get; } = CreateOsVersionInfo();

    /// <inheritdoc/>
    public CpuArchitecture CpuArchitecture { get; } = MapArchitecture(RuntimeInformation.OSArchitecture);

    /// <inheritdoc/>
    public string GpuVendor => string.Empty;

    private static OsVersionInfo CreateOsVersionInfo()
    {
        var version = Environment.OSVersion.Version;
        var build = version.Build;
        if (build <= 0)
        {
            // 兜底：从 OSDescription（形如 "Microsoft Windows 10.0.22631"）解析 Build 号
            build = ParseBuildFromDescription(RuntimeInformation.OSDescription);
        }

        return new OsVersionInfo("Windows", new Version(version.Major, version.Minor, build), build);
    }

    private static int ParseBuildFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return 0;
        }

        var segments = description.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length > 0 && int.TryParse(segments[^1], out var build))
        {
            return build;
        }

        return 0;
    }

    private static CpuArchitecture MapArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X86 => CpuArchitecture.X86,
        Architecture.X64 => CpuArchitecture.X64,
        Architecture.Arm64 => CpuArchitecture.Arm64,
        _ => CpuArchitecture.Unknown,
    };
}
