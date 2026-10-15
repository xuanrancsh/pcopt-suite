// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.Runtime.InteropServices;
using PcOpt.Core;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 内存清理抽象（最小引擎用；抽出便于单测注入替身）。
/// </summary>
public interface IMemoryCleaner
{
    /// <summary>清理内存（修剪工作集 + 清空待机列表），返回近似释放字节数。</summary>
    Result<long> Purge();
}

/// <summary>
/// Windows 内存清理实现（与产品 B 同口径）：
/// 工作集修剪 <c>psapi!EmptyWorkingSet</c>（逐进程，尽力而为）+ 待机列表清理
/// <c>ntdll!NtSetSystemInformation(SystemMemoryListInformation)</c>（需管理员）。
/// 所有失败均返回人话错误，绝不抛异常。
/// </summary>
public sealed class WindowsMemoryCleaner : IMemoryCleaner
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessSetQuota = 0x0100;

    /// <inheritdoc/>
    public Result<long> Purge()
    {
        try
        {
            var before = AvailableBytes();
            TrimWorkingSets();
            PurgeStandby();
            var after = AvailableBytes();
            return Result<long>.Ok(Math.Max(0, after - before));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Result<long>.Fail($"内存清理失败：{ex.Message}");
        }
    }

    private static long AvailableBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : 0;
    }

    private static void TrimWorkingSets()
    {
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
                    _ = EmptyWorkingSet(handle);
                }
                finally
                {
                    _ = CloseHandle(handle);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 进程已退出或被保护：跳过
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void PurgeStandby()
    {
        var command = MemoryPurgeStandbyList;
        // 需要管理员权限；失败不视为致命（工作集修剪已完成）
        _ = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
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
