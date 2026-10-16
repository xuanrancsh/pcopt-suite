// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using PcOpt.Core;
using PcOpt.Core.Native;

namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// PresentMon 采集器抽象（T32，架构 4.3）。
/// 真实实现以子进程方式运行 PresentMon 并解析其 CSV；测试注入替身产出合成帧数据，
/// 因此任何单测都不依赖网络或真实采集。
/// </summary>
public interface IPresentMonRunner
{
    /// <summary>探测 PresentMon 是否可用（不存在时给出人话提示，UI 据此优雅降级）。</summary>
    PresentMonAvailability Locate();

    /// <summary>运行一次采集，实时回传帧样本（用于实时迷你图），返回采集结果。</summary>
    /// <param name="options">采集参数。</param>
    /// <param name="progress">实时帧样本回调（可为 null）。</param>
    /// <param name="cancellationToken">取消令牌（取消即结束采集）。</param>
    Result<PresentMonCaptureResult> Capture(
        PresentMonOptions options,
        IProgress<FrameSample>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// PresentMon 子进程采集器（T32）。
/// 流程：定位可执行文件 → 启动子进程（<c>--process_name / --output_file / --timed</c>）→
/// 边采边解析 CSV（实时迷你图）→ 退出后完整解析 → 清理临时 CSV。
/// 可执行文件缺失 / 启动失败 / 退出码异常均返回明确中文错误，绝不崩溃（环境约束）。
/// </summary>
public sealed class ProcessPresentMonRunner : IPresentMonRunner
{
    /// <summary>退出等待的额外余量（秒）。</summary>
    private const int ExitGraceSeconds = 20;

    /// <summary>实时轮询间隔（毫秒）。</summary>
    private const int PollIntervalMs = 200;

    /// <summary>标准错误采集上限（字符，防异常输出撑爆内存）。</summary>
    private const int MaxCapturedErrorChars = 8_000;

    private readonly ILogger<ProcessPresentMonRunner> _logger;
    private readonly Func<string?> _resolveExecutable;

    /// <summary>初始化采集器（默认从随包目录解析 PresentMon）。</summary>
    /// <param name="logger">日志。</param>
    public ProcessPresentMonRunner(ILogger<ProcessPresentMonRunner> logger)
        : this(logger, ResourcePaths.FindPresentMonExecutable)
    {
    }

    /// <summary>初始化采集器（注入可执行文件解析器，供测试）。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="resolveExecutable">可执行文件解析器（返回 null 表示未找到）。</param>
    public ProcessPresentMonRunner(ILogger<ProcessPresentMonRunner> logger, Func<string?> resolveExecutable)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolveExecutable = resolveExecutable ?? throw new ArgumentNullException(nameof(resolveExecutable));
    }

    /// <inheritdoc/>
    public PresentMonAvailability Locate()
    {
        var path = _resolveExecutable();
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? new PresentMonAvailability(true, path, "已找到 PresentMon 采集组件，可以开始测试。")
            : new PresentMonAvailability(false, null, MissingMessage);
    }

    /// <summary>PresentMon 缺失时的获取指引（人话，含具体操作）。</summary>
    public const string MissingMessage =
        "未找到 PresentMon 采集组件（ThirdParty\\PresentMon\\PresentMon.exe）。" +
        "这是 Intel 开源的帧时间捕获工具（MIT 许可）。请在联网环境运行 " +
        "scripts\\fetch-presentmon.ps1 获取后放入程序目录下的 PresentMon 文件夹，再回来点击测试。";

    /// <inheritdoc/>
    public Result<PresentMonCaptureResult> Capture(
        PresentMonOptions options,
        IProgress<FrameSample>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.DurationSeconds <= 0)
        {
            return Result<PresentMonCaptureResult>.Fail("采集时长必须大于零秒");
        }

        var availability = Locate();
        if (!availability.Available || availability.ExecutablePath is null)
        {
            // 优雅降级：明确中文错误，绝不崩溃
            return Result<PresentMonCaptureResult>.Fail(availability.Message);
        }

        var csvPath = options.OutputCsvPath;
        try
        {
            var directory = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = availability.ExecutablePath,
                Arguments = BuildArguments(options),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // PresentMon 为原生控制台程序，stdout 按 OEM 代码页输出；
                // 固定 UTF-8 会把其中文本解成乱码。stderr 仅用于诊断，保持 UTF-8。
                StandardOutputEncoding = ConsoleOutputDecoder.OutputEncoding,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = startInfo };
            var errorBuffer = new StringBuilder();
            process.ErrorDataReceived += (_, args) => AppendCapped(errorBuffer, args.Data);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (!process.Start())
                {
                    return Result<PresentMonCaptureResult>.Fail("无法启动 PresentMon 采集进程");
                }

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

                var lastReported = 0;
                var deadline = TimeSpan.FromSeconds(options.DurationSeconds + ExitGraceSeconds);
                while (!process.HasExited && stopwatch.Elapsed < deadline && !cancellationToken.IsCancellationRequested)
                {
                    lastReported = ReportNewSamples(csvPath, lastReported, progress);
                    Thread.Sleep(PollIntervalMs);
                }

                var cancelled = cancellationToken.IsCancellationRequested;
                if (!process.HasExited)
                {
                    TryKill(process);
                }

                process.WaitForExit(5000);
                ReportNewSamples(csvPath, lastReported, progress);

                var samples = PresentMonCsvParser.ParseFile(csvPath);
                var stderr = errorBuffer.ToString().Trim();

                if (samples.Count == 0)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr)
                        ? "未捕获到任何帧（请确认游戏正在独占全屏/无边框全屏运行，并以管理员身份启动）"
                        : stderr;
                    return Result<PresentMonCaptureResult>.Fail($"PresentMon 未产出帧数据：{detail}");
                }

                _logger.LogInformation("采集完成：{Count} 帧，用时 {Seconds:F1} 秒", samples.Count, stopwatch.Elapsed.TotalSeconds);
                return Result<PresentMonCaptureResult>.Ok(new PresentMonCaptureResult(
                    samples,
                    CsvPath: null,
                    stopwatch.Elapsed,
                    cancelled ? AppendNote(stderr, "（已手动取消，结果为部分数据）") : stderr));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return Result<PresentMonCaptureResult>.Fail($"PresentMon 采集失败：{ex.Message}");
            }
        }
        finally
        {
            TryDelete(csvPath);
        }
    }

    /// <summary>构造 PresentMon 命令行参数（子进程集成方式，架构 ADR-4）。</summary>
    internal static string BuildArguments(PresentMonOptions options)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(options.ProcessName))
        {
            builder.Append("--process_name \"").Append(options.ProcessName).Append("\" ");
        }

        builder.Append("--output_file \"").Append(options.OutputCsvPath).Append("\" ");
        builder.Append("--timed ").Append(options.DurationSeconds);
        return builder.ToString();
    }

    /// <summary>解析 CSV 并回传尚未上报的新样本，返回已上报总数。</summary>
    private static int ReportNewSamples(string csvPath, int alreadyReported, IProgress<FrameSample>? progress)
    {
        if (progress is null)
        {
            return alreadyReported;
        }

        var samples = PresentMonCsvParser.ParseFile(csvPath);
        for (var i = alreadyReported; i < samples.Count; i++)
        {
            progress.Report(samples[i]);
        }

        return samples.Count;
    }

    private static void AppendCapped(StringBuilder buffer, string? text)
    {
        if (string.IsNullOrEmpty(text) || buffer.Length >= MaxCapturedErrorChars)
        {
            return;
        }

        buffer.AppendLine(text);
        if (buffer.Length > MaxCapturedErrorChars)
        {
            buffer.Length = MaxCapturedErrorChars;
        }
    }

    private static string AppendNote(string text, string note) =>
        string.IsNullOrWhiteSpace(text) ? note : text + " " + note;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // 进程可能已自行退出：忽略
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响主流程
        }
    }
}
