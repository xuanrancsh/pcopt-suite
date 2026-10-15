// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PcOpt.Core;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 产品 A（GameModePilot）命名管道探测与联动（架构 4.2：与 A 联动）。
/// 打开 <c>\\.\pipe\pcopt.gamemodepilot</c> 探测 A 是否常驻：探测成功则首页显示"开启自动守护"入口；
/// 未安装则隐藏入口并引导下载。JSON-line 协议与 A 的 <c>IpcProtocol</c> 一致。
/// </summary>
public sealed class IpcProbeService
{
    /// <summary>命名管道名（不含前缀）。</summary>
    public const string PipeName = "pcopt.gamemodepilot";

    /// <summary>完整管道路径（文档 / UI 展示用）。</summary>
    public const string PipePath = @"\\.\pipe\pcopt.gamemodepilot";

    /// <summary>探测产品 A 是否正在运行（超时即视为未运行）。</summary>
    /// <param name="timeoutMs">连接超时（毫秒）。</param>
    public bool IsRunning(int timeoutMs = 300)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(timeoutMs);
            return client.IsConnected;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 请求产品 A 启用 / 保持自动守护指定分钟数（首页"开启自动守护"入口）。
    /// 返回 A 的响应是否成功；A 未运行 / 通信失败返回失败与人话原因。
    /// </summary>
    /// <param name="minutes">保持时长（分钟）。</param>
    public Result<string> EnableGuardian(int minutes)
    {
        var payload = "{\"cmd\":\"enable\",\"minutes\":" + Math.Max(0, minutes) + "}";
        return Send(payload);
    }

    /// <summary>请求产品 A 全部还原（联动"还原中心"的一键还原）。</summary>
    public Result<string> RevertAll() => Send("{\"cmd\":\"revertAll\"}");

    private static Result<string> Send(string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(1500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            writer.WriteLine(payload);
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                return Result<string>.Fail("未收到 GameMode Pilot 的响应");
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                return Result<string>.Ok("GameMode Pilot 已开始自动守护");
            }

            var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            return Result<string>.Fail(error ?? "GameMode Pilot 拒绝了本次请求");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return Result<string>.Fail($"未能连接到 GameMode Pilot（{ex.Message}）");
        }
    }
}

/// <summary>
/// 兄弟产品启动服务（架构 4.2：B 拉起 A/C）。
/// 在程序目录 / 兄弟目录 / 源码树输出目录中探测目标可执行文件并启动；
/// 找不到时返回人话错误，UI 据此引导用户安装。
/// </summary>
public sealed class LauncherService
{
    /// <summary>产品 A 可执行文件名。</summary>
    public const string GameModePilotExecutable = "GameModePilot.exe";

    /// <summary>产品 C 可执行文件名。</summary>
    public const string FrameLabExecutable = "FrameLab.exe";

    /// <summary>启动指定可执行文件（若已存在同名进程则视为成功）。</summary>
    /// <param name="executableName">可执行文件名（如 GameModePilot.exe）。</param>
    public Result<string> Launch(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return Result<string>.Fail("目标可执行文件名为空");
        }

        var path = ResourcePaths.FindSiblingExecutable(executableName);
        if (path is null)
        {
            return Result<string>.Fail($"未找到 {executableName}，请先安装对应产品");
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Result<string>.Ok($"已启动 {Path.GetFileNameWithoutExtension(executableName)}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Result<string>.Fail($"启动 {executableName} 失败：{ex.Message}");
        }
    }
}
