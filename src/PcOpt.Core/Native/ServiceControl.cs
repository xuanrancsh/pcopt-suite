// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Runtime.InteropServices;
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Native;

/// <summary>
/// SCM 服务启动类型读写封装（架构 3.6）。
/// 修改启动类型使用 <c>ChangeServiceConfigW</c>，需要管理员权限；
/// 读取仅需 SC_MANAGER_CONNECT + SERVICE_QUERY_CONFIG（普通用户可读）。
/// </summary>
public sealed class ServiceControl
{
    /// <summary>读取服务当前启动类型。</summary>
    public Result<ServiceStartMode> GetStartType(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Result<ServiceStartMode>.Fail("服务名不能为空");
        }

        var scManager = IntPtr.Zero;
        var service = IntPtr.Zero;
        try
        {
            scManager = NativeMethods.OpenSCManagerW(null, null, NativeMethods.ScManagerConnect);
            if (scManager == IntPtr.Zero)
            {
                return Result<ServiceStartMode>.Fail($"无法连接服务控制管理器：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            service = NativeMethods.OpenServiceW(scManager, serviceName, NativeMethods.ServiceQueryConfig);
            if (service == IntPtr.Zero)
            {
                return Result<ServiceStartMode>.Fail(
                    $"无法打开服务“{serviceName}”：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            // 第一次调用获取所需缓冲区大小
            _ = NativeMethods.QueryServiceConfigW(service, IntPtr.Zero, 0, out var neededBytes);
            if (neededBytes == 0)
            {
                return Result<ServiceStartMode>.Fail($"无法查询服务配置“{serviceName}”：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            var buffer = Marshal.AllocHGlobal((int)neededBytes);
            try
            {
                if (!NativeMethods.QueryServiceConfigW(service, buffer, neededBytes, out _))
                {
                    return Result<ServiceStartMode>.Fail(
                        $"无法查询服务配置“{serviceName}”：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
                }

                // QUERY_SERVICE_CONFIG 布局：dwServiceType(0) / dwStartType(4) / dwErrorControl(8) ...
                var rawStartType = Marshal.ReadInt32(buffer, NativeMethods.QueryServiceConfigStartTypeOffset);
                if (!Enum.IsDefined((ServiceStartMode)rawStartType))
                {
                    return Result<ServiceStartMode>.Fail($"服务“{serviceName}”返回未知启动类型（{rawStartType}）");
                }

                return Result<ServiceStartMode>.Ok((ServiceStartMode)rawStartType);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            if (service != IntPtr.Zero)
            {
                _ = NativeMethods.CloseServiceHandle(service);
            }

            if (scManager != IntPtr.Zero)
            {
                _ = NativeMethods.CloseServiceHandle(scManager);
            }
        }
    }

    /// <summary>
    /// 修改服务启动类型（<c>ChangeServiceConfigW</c>，仅改启动类型字段，其余保留）。
    /// </summary>
    public Result SetStartType(string serviceName, ServiceStartMode target)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Result.Fail("服务名不能为空");
        }

        var scManager = IntPtr.Zero;
        var service = IntPtr.Zero;
        try
        {
            scManager = NativeMethods.OpenSCManagerW(null, null, NativeMethods.ScManagerConnect);
            if (scManager == IntPtr.Zero)
            {
                return Result.Fail($"无法连接服务控制管理器：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            service = NativeMethods.OpenServiceW(scManager, serviceName, NativeMethods.ServiceQueryConfig | NativeMethods.ServiceChangeConfig);
            if (service == IntPtr.Zero)
            {
                return Result.Fail(
                    $"无法打开服务“{serviceName}”（修改启动类型需要管理员权限）：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            var changed = NativeMethods.ChangeServiceConfigW(
                hService: service,
                dwServiceType: NativeMethods.ServiceNoChange,
                dwStartType: (uint)target,
                dwErrorControl: NativeMethods.ServiceNoChange,
                lpBinaryPathName: null,
                lpLoadOrderGroup: null,
                lpdwTagId: IntPtr.Zero,
                lpDependencies: null,
                lpServiceStartName: null,
                lpPassword: null,
                lpDisplayName: null);
            if (!changed)
            {
                return Result.Fail($"无法修改服务“{serviceName}”启动类型：{NativeMethods.FormatWin32Error(Marshal.GetLastWin32Error())}");
            }

            return Result.Ok();
        }
        finally
        {
            if (service != IntPtr.Zero)
            {
                _ = NativeMethods.CloseServiceHandle(service);
            }

            if (scManager != IntPtr.Zero)
            {
                _ = NativeMethods.CloseServiceHandle(scManager);
            }
        }
    }
}
