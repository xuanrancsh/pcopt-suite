// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Platform;

namespace PcOpt.Core.Tests.TestDoubles;

/// <summary>固定系统环境替身（不触碰真实系统）。</summary>
internal sealed class TestSystemEnvironment : ISystemEnvironment
{
    public TestSystemEnvironment(OsVersionInfo osVersion, CpuArchitecture cpuArchitecture, string gpuVendor = "")
    {
        OsVersion = osVersion;
        CpuArchitecture = cpuArchitecture;
        GpuVendor = gpuVendor;
    }

    /// <summary>Win11 23H2（Build 22631）+ X64 + GPU 未知 的默认环境。</summary>
    public static TestSystemEnvironment Win11X64() =>
        new(new OsVersionInfo("Windows", new Version(10, 0, 22631), 22631), CpuArchitecture.X64);

    public OsVersionInfo OsVersion { get; }

    public CpuArchitecture CpuArchitecture { get; }

    public string GpuVendor { get; }
}
