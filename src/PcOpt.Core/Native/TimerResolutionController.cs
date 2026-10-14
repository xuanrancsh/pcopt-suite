// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using PcOpt.Core.Tweak;

namespace PcOpt.Core.Native;

/// <summary>
/// 计时器分辨率控制器（ntdll <c>NtSetTimerResolution</c> / <c>NtQueryTimerResolution</c>，架构 3.6）。
/// 分辨率单位为 100ns（0.5ms = 5000）。
/// 注意：<c>NtQueryTimerResolution</c> 的两个输出参数语义为（最大可设/最粗, 最小可设/最细, 当前值），
/// 本控制器对外统一暴露为（Minimum100Ns=最细, Maximum100Ns=最粗, Current100Ns=当前）。
/// 请求为进程级，进程退出时系统自动回收；正常还原流程由 <c>TimerResolutionActionHandler</c> 完成。
/// </summary>
public sealed class TimerResolutionController
{
    /// <summary>0.5ms（100ns 单位），典型用户态可达最高精度。</summary>
    public const uint HalfMillisecond100Ns = 5000;

    /// <summary>NTSTATUS 成功。</summary>
    private const int StatusSuccess = 0;

    /// <summary>查询系统支持范围与当前分辨率。</summary>
    public Result<(uint Minimum100Ns, uint Maximum100Ns, uint Current100Ns)> QueryResolution()
    {
        // 原生输出顺序：（最粗, 最细, 当前）
        var status = NativeMethods.NtQueryTimerResolution(out var coarsest100Ns, out var finest100Ns, out var current100Ns);
        if (status != StatusSuccess)
        {
            return Result<(uint, uint, uint)>.Fail($"无法查询计时器分辨率（NTSTATUS 0x{status:X8}）");
        }

        return Result<(uint, uint, uint)>.Ok((finest100Ns, coarsest100Ns, current100Ns));
    }

    /// <summary>请求指定分辨率（幂等：已是目标值同样返回成功）。</summary>
    public Result<uint> Set(uint desiredResolution100Ns)
    {
        var range = QueryResolution();
        if (!range.IsSuccess)
        {
            return Result<uint>.Fail(range.Error);
        }

        var (minimum, maximum, _) = range.Value;
        if (desiredResolution100Ns < minimum || desiredResolution100Ns > maximum)
        {
            return Result<uint>.Fail(
                $"计时器分辨率 {desiredResolution100Ns.ToString(System.Globalization.CultureInfo.InvariantCulture)} 超出系统支持范围（{minimum} ~ {maximum}，100ns 单位）");
        }

        var status = NativeMethods.NtSetTimerResolution(desiredResolution100Ns, setResolution: true, out var current);
        if (status != StatusSuccess)
        {
            return Result<uint>.Fail($"无法设置计时器分辨率（NTSTATUS 0x{status:X8}）");
        }

        return Result<uint>.Ok(current);
    }

    /// <summary>释放本进程的计时器分辨率请求。</summary>
    public Result Release()
    {
        var status = NativeMethods.NtSetTimerResolution(0, setResolution: false, out _);
        return status == StatusSuccess
            ? Result.Ok()
            : Result.Fail($"无法释放计时器分辨率请求（NTSTATUS 0x{status:X8}）");
    }
}
