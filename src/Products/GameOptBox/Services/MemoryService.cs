// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Runtime.InteropServices;
using PcOpt.Core;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>内存状态快照。</summary>
/// <param name="TotalPhysicalBytes">物理内存总量（字节）。</param>
/// <param name="AvailableBytes">可用物理内存（字节）。</param>
public sealed record MemoryStatus(long TotalPhysicalBytes, long AvailableBytes)
{
    /// <summary>已用物理内存（字节）。</summary>
    public long UsedBytes => Math.Max(0, TotalPhysicalBytes - AvailableBytes);
}

/// <summary>内存清理结果。</summary>
/// <param name="FreedBytes">本次释放的物理内存（近似，字节）。</param>
/// <param name="After">清理后的内存状态。</param>
public sealed record MemoryTrimResult(long FreedBytes, MemoryStatus After);

/// <summary>
/// 内存管理抽象（T27，PRD B-P0-7：待机列表清理 / 工作集修剪 / 一键清理）。
/// 抽象出来便于单元测试替换为替身。
/// </summary>
public interface IMemoryManager
{
    /// <summary>查询当前内存状态。</summary>
    MemoryStatus Query();

    /// <summary>修剪全部进程工作集（把不常用内存页交还系统）。</summary>
    Result<MemoryTrimResult> TrimWorkingSets();

    /// <summary>清空待机列表（Standby List）。</summary>
    Result<MemoryTrimResult> PurgeStandbyList();
}

/// <summary>
/// Windows 内存管理实现。
/// 工作集修剪：<c>psapi!EmptyWorkingSet</c>（逐进程，尽力而为，跳过受保护进程）；
/// 待机列表清理：<c>ntdll!NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList)</c>（需管理员）。
/// 所有调用失败均返回人话错误，绝不抛异常（架构 8.2）。
/// </summary>
public sealed class WindowsMemoryManager : IMemoryManager
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    /// <inheritdoc/>
    public MemoryStatus Query()
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        if (GlobalMemoryStatusEx(ref status))
        {
            return new MemoryStatus((long)status.TotalPhys, (long)status.AvailPhys);
        }

        return new MemoryStatus(0, 0);
    }

    /// <inheritdoc/>
    public Result<MemoryTrimResult> TrimWorkingSets()
    {
        var before = Query();
        var trimmed = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var handle = OpenProcess(ProcessQueryInformation | ProcessSetQuota, false, process.Id);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    if (EmptyWorkingSet(handle))
                    {
                        trimmed++;
                    }
                }
                finally
                {
                    _ = CloseHandle(handle);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 进程已退出或被保护，跳过
            }
            finally
            {
                process.Dispose();
            }
        }

        var after = Query();
        var freed = Math.Max(0, after.AvailableBytes - before.AvailableBytes);
        return Result<MemoryTrimResult>.Ok(
            new MemoryTrimResult(freed, after));
    }

    /// <inheritdoc/>
    public Result<MemoryTrimResult> PurgeStandbyList()
    {
        var before = Query();
        var command = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        if (status != 0)
        {
            return Result<MemoryTrimResult>.Fail("清理待机列表失败（需要管理员权限）");
        }

        var after = Query();
        var freed = Math.Max(0, after.AvailableBytes - before.AvailableBytes);
        return Result<MemoryTrimResult>.Ok(new MemoryTrimResult(freed, after));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);
}
