// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using PcOpt.Core;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// DPC / 中断延迟采样抽象（T33 / PRD C-P0-3）。抽象出来便于单测注入固定值替身。
/// </summary>
public interface IDpcSampler
{
    /// <summary>在给定时长内采样系统调度 / 中断延迟。</summary>
    /// <param name="duration">采样时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>
/// 近似 DPC / 中断延迟采样器。
/// <para>
/// <b>口径说明（诚实标注）</b>：真正的 DPC/ISR 延迟需要 ETW（<c>Microsoft-Windows-Kernel-*</c>）或
/// 专用驱动，本工具在无管理员/ETW 场景下采用<b>调度抖动近似</b>——循环请求 1ms 定时并测量实际
/// 超时量，超时量越小说明系统越"跟得上"，可间接反映中断/调度对玩家体验的干扰。
/// 该值<b>不等价于</b> DPC 真实耗时，报告中必须如实标注口径，不得据此夸大结论。
/// </para>
/// </summary>
public sealed class SchedulingJitterDpcSampler : IDpcSampler
{
    /// <summary>每次采样的目标间隔（毫秒）。</summary>
    private const double TargetIntervalMs = 1.0;

    /// <inheritdoc/>
    public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            return Result<DpcStats>.Fail("采样时长必须大于零");
        }

        try
        {
            var total = 0.0;
            var max = 0.0;
            var count = 0;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                var start = Stopwatch.GetTimestamp();
                Thread.Sleep(1);
                var elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                var overshoot = Math.Max(0, elapsedMs - TargetIntervalMs);

                total += overshoot;
                if (overshoot > max)
                {
                    max = overshoot;
                }

                count++;
            }

            return Result<DpcStats>.Ok(new DpcStats(
                count > 0 ? total / count : 0,
                max,
                count));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ThreadInterruptedException)
        {
            return Result<DpcStats>.Fail($"DPC 延迟采样失败：{ex.Message}");
        }
    }
}
