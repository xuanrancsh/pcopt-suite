// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Runtime.InteropServices;

namespace PcOpt.Core.Native;

/// <summary>
/// 计划任务控制器（Task Scheduler COM 互操作，架构 3.6）。
/// 采用零依赖的 dynamic COM 调度（Schedule.Service，双接口支持 IDispatch），
/// 不引入任何 NuGet 包；若未来需要更强类型安全可再评估 early-bound 互操作集。
/// 任务路径使用完整路径（如 <c>\Microsoft\Windows\Maintenance\BrIdleTask</c>）。
/// </summary>
public sealed class ScheduledTaskController
{
    /// <summary>Task Scheduler COM ProgID。</summary>
    public const string TaskSchedulerProgId = "Schedule.Service";

    /// <summary>ERROR_FILE_NOT_FOUND（Task Scheduler 对不存在任务返回该错误码）。</summary>
    private const int ErrorFileNotFound = unchecked((int)0x80070002);

    /// <summary>
    /// 查询计划任务启用状态。
    /// 返回 Ok(null) 表示任务不存在；Ok(true/false) 为当前启用状态。
    /// </summary>
    public Result<bool?> QueryEnabled(string taskPath)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return Result<bool?>.Fail("计划任务路径不能为空");
        }

        try
        {
            var serviceType = Type.GetTypeFromProgID(TaskSchedulerProgId);
            if (serviceType is null)
            {
                return Result<bool?>.Fail("当前系统不支持计划任务服务（Schedule.Service 不可用）");
            }

            dynamic service = Activator.CreateInstance(serviceType)!;
            try
            {
                service.Connect();
                var (folderPath, taskName) = SplitTaskPath(taskPath);
                dynamic folder = service.GetFolder(folderPath);
                dynamic task = folder.GetTask(taskName);
                return Result<bool?>.Ok((bool?)task.Enabled);
            }
            catch (Exception ex) when ((ex is COMException || ex is FileNotFoundException) && ex.HResult == ErrorFileNotFound)
            {
                // 任务不存在（含父文件夹不存在）——语义为"无任务可管"。
                // 注意：dynamic 运行时绑定下 Task Scheduler 对不存在任务抛出的是
                // System.IO.FileNotFoundException（0x80070002）而非 COMException，两者都需捕获。
                return Result<bool?>.Ok(null);
            }
            finally
            {
                _ = Marshal.FinalReleaseComObject(service);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or TypeLoadException)
        {
            return Result<bool?>.Fail($"无法访问计划任务“{taskPath}”：{ex.Message}");
        }
    }

    /// <summary>
    /// 启用 / 禁用计划任务。任务不存在时返回失败（明确错误）。
    /// </summary>
    public Result SetEnabled(string taskPath, bool enable)
    {
        if (string.IsNullOrWhiteSpace(taskPath))
        {
            return Result.Fail("计划任务路径不能为空");
        }

        try
        {
            var serviceType = Type.GetTypeFromProgID(TaskSchedulerProgId);
            if (serviceType is null)
            {
                return Result.Fail("当前系统不支持计划任务服务（Schedule.Service 不可用）");
            }

            dynamic service = Activator.CreateInstance(serviceType)!;
            try
            {
                service.Connect();
                var (folderPath, taskName) = SplitTaskPath(taskPath);
                dynamic folder = service.GetFolder(folderPath);
                dynamic task = folder.GetTask(taskName);
                task.Enabled = enable;
                return Result.Ok();
            }
            catch (Exception ex) when ((ex is COMException || ex is FileNotFoundException) && ex.HResult == ErrorFileNotFound)
            {
                // dynamic 绑定下不存在任务抛 FileNotFoundException（0x80070002），与 COMException 同等对待
                return Result.Fail($"计划任务不存在：{taskPath}");
            }
            finally
            {
                _ = Marshal.FinalReleaseComObject(service);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException or TypeLoadException)
        {
            return Result.Fail($"无法修改计划任务“{taskPath}”：{ex.Message}");
        }
    }

    /// <summary>将完整任务路径拆分为（文件夹路径, 任务名）。</summary>
    internal static (string FolderPath, string TaskName) SplitTaskPath(string taskPath)
    {
        var normalized = taskPath.Trim().Replace('/', '\\');
        var lastSeparator = normalized.LastIndexOf('\\');
        if (lastSeparator <= 0)
        {
            return ("\\", normalized.TrimStart('\\'));
        }

        var folderPath = normalized[..lastSeparator];
        var taskName = normalized[(lastSeparator + 1)..];
        return (string.IsNullOrEmpty(folderPath) ? "\\" : folderPath, taskName);
    }
}
