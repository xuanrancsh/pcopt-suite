// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using Microsoft.Extensions.Logging;
using PcOpt.Core;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 采集编排服务（T32/T33）：定位采集器 → 启动 PresentMon 子进程 → 实时回传帧样本 →
/// 并发采样 DPC 延迟 → 分析产出 <see cref="CaptureSession"/>。
/// 采集器缺失时提前返回明确中文错误（不再进入采集流程）。
/// </summary>
public sealed class CaptureService
{
    /// <summary>采集结束后等待 DPC 采样返回的上限（毫秒；采样器已收到取消信号应尽快返回）。</summary>
    private const int DpcWaitMilliseconds = 2_000;

    private readonly IPresentMonRunner _runner;
    private readonly IDpcSampler _dpcSampler;
    private readonly ILogger<CaptureService> _logger;

    /// <summary>初始化采集服务。</summary>
    public CaptureService(IPresentMonRunner runner, IDpcSampler dpcSampler, ILogger<CaptureService> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _dpcSampler = dpcSampler ?? throw new ArgumentNullException(nameof(dpcSampler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>当前采集可用性（供 UI 提前提示"未安装 PresentMon"）。</summary>
    public PresentMonAvailability Availability => _runner.Locate();

    /// <summary>
    /// 运行一次采集并分析。
    /// </summary>
    /// <param name="processName">目标进程名（可空：采集全屏前台）。</param>
    /// <param name="durationSeconds">采集时长（秒）。</param>
    /// <param name="label">会话标签（如"优化前"/"优化后"）。</param>
    /// <param name="progress">实时帧样本回调（实时迷你图）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Result<CaptureSession> RunCapture(
        string processName,
        int durationSeconds,
        string label,
        IProgress<FrameSample>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (durationSeconds <= 0)
        {
            return Result<CaptureSession>.Fail("采集时长必须大于零秒");
        }

        var availability = _runner.Locate();
        if (!availability.Available)
        {
            // 优雅降级：明确中文提示，绝不崩溃
            return Result<CaptureSession>.Fail(availability.Message);
        }

        var csvPath = Path.Combine(ResourcePaths.CapturesDirectory, "capture-" + Guid.NewGuid().ToString("N") + ".csv");
        var options = new PresentMonOptions(processName ?? string.Empty, durationSeconds, csvPath);

        // DPC 延迟采样与采集并发进行（互不阻塞；采样失败不阻断主流程）。
        // 注意：不把取消令牌传给 Task.Run —— 否则采集很快结束时（取消早于委托被调度）
        // 任务会被判定为"取消"而丢失本可返回的部分结果；改为由采样器观察令牌自行尽早返回。
        var dpcSeconds = Math.Clamp(durationSeconds, 1, 15);

        // 加固 D4：不在此处用 using —— 有界等待超时后采样器可能仍在后台运行，若此刻释放 CTS，
        // 采样器再访问令牌源理论上可能抛 ObjectDisposedException。故统一在"任务真正结束后"再释放，
        // 并始终观察其异常，保持"零未观察异常"。
        var dpcCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var dpcTask = Task.Run(() => _dpcSampler.Sample(TimeSpan.FromSeconds(dpcSeconds), dpcCts.Token));

        Result<PresentMonCaptureResult> capture;
        try
        {
            capture = _runner.Capture(options, progress, cancellationToken);
        }
        finally
        {
            // 采集结束即请求停止 DPC 采样（采样器会带上已采到的部分返回）
            dpcCts.Cancel();
        }

        if (!capture.IsSuccess)
        {
            // 采集失败 / 已取消：不等待 DPC 采样（保证调用方取消语义即时返回），仅安全收尾。
            CompleteDpcTask(dpcTask, dpcCts, wait: false);
            return Result<CaptureSession>.Fail(capture.Error);
        }

        // 有界等待：采样器因上面的取消应尽快返回；万一未及时返回也不阻塞主流程。
        var dpc = CompleteDpcTask(dpcTask, dpcCts, wait: true);

        var samples = capture.Value!.Samples;
        var stats = FrameAnalyzer.Analyze(samples, dpc);
        var session = new CaptureSession(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            processName ?? string.Empty,
            durationSeconds,
            samples,
            stats,
            label);

        _logger.LogInformation("已生成测试会话：{Count} 帧，平均 {Fps:F0} FPS", stats.FrameCount, stats.AverageFps);
        return Result<CaptureSession>.Ok(session);
    }

    /// <summary>
    /// 收尾 DPC 采样任务（加固 D4）：可选地有界等待并取其统计；随后<b>始终</b>观察任务异常，
    /// 且仅在任务真正结束后释放令牌源（任务仍在后台运行时改为在其续延中释放），
    /// 从而避免"释放 CTS 后采样器仍访问令牌源"与"未观察任务异常"两类问题。
    /// </summary>
    /// <param name="task">DPC 采样任务。</param>
    /// <param name="cts">该任务使用的取消令牌源（本方法负责释放）。</param>
    /// <param name="wait">是否进行有界等待并返回统计（采集失败时为 false，保证即时返回）。</param>
    /// <returns>有界等待成功时返回采样统计，否则返回 <see cref="DpcStats.Empty"/>。</returns>
    private static DpcStats CompleteDpcTask(Task<Result<DpcStats>> task, CancellationTokenSource cts, bool wait)
    {
        var dpc = DpcStats.Empty;
        if (wait)
        {
            try
            {
                if (task.Wait(DpcWaitMilliseconds) && task.IsCompletedSuccessfully && task.Result.IsSuccess)
                {
                    dpc = task.Result.Value!;
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or AggregateException)
            {
                dpc = DpcStats.Empty;
            }
        }

        if (task.IsCompleted)
        {
            // 读取 Exception 即标记为已观察（成功 / 取消时为 null），避免未观察任务异常
            _ = task.Exception;
            cts.Dispose();
        }
        else
        {
            // 任务仍在后台运行：等它结束后再释放令牌源，并在同一次续延里观察其异常
            task.ContinueWith(
                completed =>
                {
                    _ = completed.Exception;
                    cts.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return dpc;
    }
}
