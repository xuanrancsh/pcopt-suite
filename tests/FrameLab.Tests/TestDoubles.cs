// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Runtime.ExceptionServices;
using PcOpt.Core;
using PcOpt.Core.Tweak;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>
/// 测试用 PresentMon 采集替身：可配置可用性与合成帧样本，记录最近一次采集参数。
/// 让全部单测都脱离网络与真实采集（环境约束）。
/// </summary>
internal sealed class FakePresentMonRunner : IPresentMonRunner
{
    private readonly PresentMonAvailability _availability;
    private readonly IReadOnlyList<FrameSample> _samples;

    /// <summary>初始化替身。</summary>
    /// <param name="available">采集器是否可用。</param>
    /// <param name="samples">采集返回的帧样本（默认空）。</param>
    public FakePresentMonRunner(bool available = true, IReadOnlyList<FrameSample>? samples = null)
    {
        _samples = samples ?? Array.Empty<FrameSample>();
        _availability = available
            ? new PresentMonAvailability(true, @"C:\fake\PresentMon.exe", "可用")
            : new PresentMonAvailability(false, null, "未找到 PresentMon 采集组件（测试替身）");
    }

    /// <summary>最近一次采集使用的参数。</summary>
    public PresentMonOptions? LastOptions { get; private set; }

    /// <summary>Capture 被调用的次数。</summary>
    public int CaptureCount { get; private set; }

    /// <inheritdoc/>
    public PresentMonAvailability Locate() => _availability;

    /// <inheritdoc/>
    public Result<PresentMonCaptureResult> Capture(
        PresentMonOptions options,
        IProgress<FrameSample>? progress,
        CancellationToken cancellationToken)
    {
        LastOptions = options;
        CaptureCount++;

        if (!_availability.Available)
        {
            return Result<PresentMonCaptureResult>.Fail(_availability.Message);
        }

        foreach (var sample in _samples)
        {
            progress?.Report(sample);
        }

        return Result<PresentMonCaptureResult>.Ok(new PresentMonCaptureResult(
            _samples,
            options.OutputCsvPath,
            TimeSpan.FromSeconds(Math.Max(1, options.DurationSeconds)),
            string.Empty));
    }
}

/// <summary>测试用 DPC 采样替身（返回固定统计）。</summary>
internal sealed class FakeDpcSampler : IDpcSampler
{
    private readonly DpcStats _stats;

    /// <summary>初始化替身。</summary>
    /// <param name="stats">固定返回的统计（默认空）。</param>
    public FakeDpcSampler(DpcStats? stats = null) => _stats = stats ?? DpcStats.Empty;

    /// <inheritdoc/>
    public Result<DpcStats> Sample(TimeSpan duration, CancellationToken cancellationToken) =>
        Result<DpcStats>.Ok(_stats);
}

/// <summary>测试用产品 A 客户端替身。</summary>
internal sealed class FakeGameModePilotClient : IGameModePilotClient
{
    /// <summary>是否"在运行"。</summary>
    public bool Running { get; set; }

    /// <summary>ApplyProfile 的返回值（默认成功）。</summary>
    public Result<string> ApplyResult { get; set; } = Result<string>.Ok("已接受");

    /// <summary>RevertAll 的返回值（默认成功）。</summary>
    public Result<string> RevertResult { get; set; } = Result<string>.Ok("已还原");

    /// <summary>最近一次 applyProfile 的规则 Id。</summary>
    public IReadOnlyList<string>? LastRuleIds { get; private set; }

    /// <inheritdoc/>
    public bool IsRunning(int timeoutMs = 300) => Running;

    /// <inheritdoc/>
    public Result<string> ApplyProfile(IReadOnlyList<string> ruleIds)
    {
        LastRuleIds = ruleIds;
        return ApplyResult;
    }

    /// <inheritdoc/>
    public Result<string> RevertAll() => RevertResult;
}

/// <summary>测试用内存清理替身（不触碰系统）。</summary>
internal sealed class FakeMemoryCleaner : IMemoryCleaner
{
    /// <summary>Purge 返回值（默认成功释放 0 字节）。</summary>
    public Result<long> PurgeResult { get; set; } = Result<long>.Ok(0);

    /// <summary>Purge 被调用次数。</summary>
    public int PurgeCount { get; private set; }

    /// <inheritdoc/>
    public Result<long> Purge()
    {
        PurgeCount++;
        return PurgeResult;
    }
}

/// <summary>测试用系统信号提供者替身。</summary>
internal sealed class FakeSystemSignalProvider : ISystemSignalProvider
{
    /// <summary>返回的信号（默认全 false、HAGS 开、MPO 关）。</summary>
    public SystemSignals Signals { get; set; } = new(
        StandbyListExpanded: false,
        CpuThrottling: false,
        GpuBottleneck: false,
        HagsEnabled: true,
        MpoEnabled: false,
        GpuVendor: "NVIDIA");

    /// <inheritdoc/>
    public SystemSignals Read() => Signals;
}

/// <summary>测试用通知收集器。</summary>
internal sealed class RecordingNotificationService : INotificationService
{
    private readonly List<string> _messages = new();

    /// <summary>已推送的通知（按顺序）。</summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <inheritdoc/>
    public event EventHandler<string>? ToastRequested;

    /// <inheritdoc/>
    public void Notify(string message)
    {
        _messages.Add(message);
        ToastRequested?.Invoke(this, message);
    }
}

/// <summary>同步进度收集器（避免 <see cref="Progress{T}"/> 跨线程投递带来的不确定性）。</summary>
/// <typeparam name="T">进度值类型。</typeparam>
internal sealed class CollectingProgress<T> : IProgress<T>
{
    /// <summary>已上报的值（按顺序）。</summary>
    public List<T> Items { get; } = new();

    /// <inheritdoc/>
    public void Report(T value) => Items.Add(value);
}

/// <summary>测试用动作处理器替身：记录被应用的动作，全部返回成功（不触碰系统）。</summary>
internal sealed class FakeTweakActionHandler : ITweakActionHandler
{
    /// <summary>已被 <see cref="Apply"/> 的动作。</summary>
    public List<TweakAction> Applied { get; } = new();

    /// <inheritdoc/>
    public bool CanHandle(TweakAction action) => true;

    /// <inheritdoc/>
    public Result<string> CaptureOriginal(TweakAction action) => Result<string>.Ok("{}");

    /// <inheritdoc/>
    public Result Apply(TweakAction action)
    {
        Applied.Add(action);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public Result Revert(TweakAction action, string originalValueJson) => Result.Ok();

    /// <inheritdoc/>
    public Result Verify(TweakAction action) => Result.Ok();
}

/// <summary>
/// 在<b>单个常驻 STA 线程</b>上执行委托（WPF 绘图 / PNG 导出需要 STA）。
/// <para>
/// 采用常驻线程而非"每次新建 STA 线程"，是因为 <c>ReportPngRenderer</c> 持有静态 WPF 画刷：
/// 每次新建线程会让"在 A 线程创建、在 B 线程使用"触发 Freezable 跨线程异常（源码缺陷 D1）。
/// 常驻同一线程可规避该问题，同时**不掩盖**缺陷本身（D1 另有独立断言）。
/// 所有测试类并发调用都会串行汇入同一 STA 线程，天然消除跨 STA 竞态。
/// </para>
/// </summary>
internal static class StaRunner
{
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = new();
    private static readonly Thread Worker;

    static StaRunner()
    {
        Worker = new Thread(() =>
        {
            foreach (var work in Queue.GetConsumingEnumerable())
            {
                work();
            }
        })
        {
            IsBackground = true,
            Name = "test-sta-worker",
        };
        Worker.SetApartmentState(ApartmentState.STA);
        Worker.Start();
    }

    /// <summary>在常驻 STA 线程执行动作并等待完成；异常原样抛出到调用线程。</summary>
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        ExceptionDispatchInfo? captured = null;
        using var done = new ManualResetEventSlim(false);
        Queue.Add(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        captured?.Throw();
    }
}
