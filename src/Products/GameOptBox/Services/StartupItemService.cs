// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using PcOpt.Core;
using PcOpt.Core.Native;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 启动项管理服务（T23，PRD B-P0-3）：注册表 Run + 启动文件夹 + 计划任务三路扫描；
/// 启用 / 禁用 / 可逆（禁用注册表项=挪到备份键；禁用启动文件夹项=改名；禁用计划任务=SetEnabled(false)）。
/// 禁用绝不删除，随时可一键恢复（零误删承诺）。
/// </summary>
public sealed class StartupItemService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyPathWow = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledBackupKeyPath = @"Software\pcopt\DisabledRun";
    private const string DisabledFileSuffix = ".pcopt-disabled";

    private readonly ScheduledTaskController _scheduledTasks;
    private readonly ILogger<StartupItemService> _logger;

    /// <summary>初始化启动项服务。</summary>
    public StartupItemService(ScheduledTaskController scheduledTasks, ILogger<StartupItemService> logger)
    {
        _scheduledTasks = scheduledTasks ?? throw new ArgumentNullException(nameof(scheduledTasks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>三路扫描启动项（单路失败不影响其余，尽力而为）。</summary>
    public Result<IReadOnlyList<StartupItem>> Scan()
    {
        var items = new List<StartupItem>();
        items.AddRange(ScanRegistry(Registry.CurrentUser, StartupSource.RegistryCurrentUser, RunKeyPath));
        items.AddRange(ScanRegistry(Registry.LocalMachine, StartupSource.RegistryLocalMachine, RunKeyPath));
        items.AddRange(ScanRegistry(Registry.LocalMachine, StartupSource.RegistryLocalMachine, RunKeyPathWow));
        items.AddRange(ScanStartupFolders());
        items.AddRange(ScanScheduledTasks());

        return Result<IReadOnlyList<StartupItem>>.Ok(
            items.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList());
    }

    /// <summary>禁用一条启动项（可逆）。</summary>
    public Result Disable(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Source switch
        {
            StartupSource.RegistryCurrentUser => MoveRegistryValue(item, Registry.CurrentUser, toBackup: true),
            StartupSource.RegistryLocalMachine => MoveRegistryValue(item, Registry.LocalMachine, toBackup: true),
            StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon => RenameStartupFile(item, disable: true),
            StartupSource.ScheduledTask => _scheduledTasks.SetEnabled(item.Command, enable: false),
            _ => Result.Fail($"不支持的启动项来源：{item.Source}"),
        };
    }

    /// <summary>重新启用一条启动项。</summary>
    public Result Enable(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Source switch
        {
            StartupSource.RegistryCurrentUser => MoveRegistryValue(item, Registry.CurrentUser, toBackup: false),
            StartupSource.RegistryLocalMachine => MoveRegistryValue(item, Registry.LocalMachine, toBackup: false),
            StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon => RenameStartupFile(item, disable: false),
            StartupSource.ScheduledTask => _scheduledTasks.SetEnabled(item.Command, enable: true),
            _ => Result.Fail($"不支持的启动项来源：{item.Source}"),
        };
    }

    private List<StartupItem> ScanRegistry(RegistryKey hive, StartupSource source, string subKeyPath)
    {
        var items = new List<StartupItem>();
        try
        {
            using var runKey = hive.OpenSubKey(subKeyPath, writable: false);
            if (runKey is not null)
            {
                foreach (var valueName in runKey.GetValueNames())
                {
                    var command = runKey.GetValue(valueName)?.ToString() ?? string.Empty;
                    items.Add(BuildItem(source, valueName, command, enabled: true));
                }
            }

            using var backupKey = hive.OpenSubKey(DisabledBackupKeyPath, writable: false);
            if (backupKey is not null)
            {
                foreach (var valueName in backupKey.GetValueNames())
                {
                    var command = backupKey.GetValue(valueName)?.ToString() ?? string.Empty;
                    items.Add(BuildItem(source, valueName, command, enabled: false));
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogDebug("扫描注册表启动项失败（{Source}）：{Message}", source, ex.Message);
        }

        return items;
    }

    private List<StartupItem> ScanStartupFolders()
    {
        var items = new List<StartupItem>();
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.StartupFolderUser);
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.StartupFolderCommon);
        return items;
    }

    private static void AddFolder(List<StartupItem> items, string folder, StartupSource source)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            var isDisabled = fileName.EndsWith(DisabledFileSuffix, StringComparison.OrdinalIgnoreCase);
            var name = isDisabled ? fileName[..^DisabledFileSuffix.Length] : fileName;
            items.Add(new StartupItem(
                $"{source}|{name}",
                name,
                file,
                source,
                !isDisabled,
                string.Empty));
        }
    }

    private List<StartupItem> ScanScheduledTasks()
    {
        var items = new List<StartupItem>();
        var tasksRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
        if (!Directory.Exists(tasksRoot))
        {
            return items;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(tasksRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(tasksRoot, file).Replace('/', '\\');
                var taskPath = "\\" + relative;
                var taskName = Path.GetFileNameWithoutExtension(file);
                var enabled = _scheduledTasks.QueryEnabled(taskPath);
                items.Add(new StartupItem(
                    $"task|{taskPath}",
                    taskName,
                    taskPath,
                    StartupSource.ScheduledTask,
                    enabled.IsSuccess && enabled.Value == true,
                    string.Empty));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogDebug("扫描计划任务失败：{Message}", ex.Message);
        }

        return items;
    }

    private static StartupItem BuildItem(StartupSource source, string valueName, string command, bool enabled) =>
        new($"{source}|{valueName}", valueName, command, source, enabled, ResolvePublisher(command));

    private Result MoveRegistryValue(StartupItem item, RegistryKey hive, bool toBackup)
    {
        try
        {
            var sourcePath = toBackup ? RunKeyPath : DisabledBackupKeyPath;
            var targetPath = toBackup ? DisabledBackupKeyPath : RunKeyPath;
            using var sourceKey = hive.OpenSubKey(sourcePath, writable: true);
            if (sourceKey is null)
            {
                return Result.Fail($"未找到启动项“{item.Name}”所在的注册表位置");
            }

            var command = sourceKey.GetValue(item.Name)?.ToString();
            if (command is null)
            {
                return Result.Fail($"未找到启动项“{item.Name}”");
            }

            using var targetKey = hive.CreateSubKey(targetPath, writable: true);
            targetKey.SetValue(item.Name, command, RegistryValueKind.String);
            sourceKey.DeleteValue(item.Name, throwOnMissingValue: false);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return Result.Fail($"{(toBackup ? "禁用" : "启用")}启动项“{item.Name}”失败：{ex.Message}（可能需要管理员权限）");
        }
    }

    private static Result RenameStartupFile(StartupItem item, bool disable)
    {
        try
        {
            if (disable)
            {
                if (!File.Exists(item.Command))
                {
                    return Result.Fail($"未找到启动项文件“{item.Name}”");
                }

                File.Move(item.Command, item.Command + DisabledFileSuffix);
                return Result.Ok();
            }

            // 启用：从当前路径（可能已带后缀）还原
            var current = item.Command;
            if (!File.Exists(current))
            {
                current = item.Command + DisabledFileSuffix;
            }

            if (!File.Exists(current))
            {
                return Result.Fail($"未找到启动项文件“{item.Name}”");
            }

            var target = current.EndsWith(DisabledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? current[..^DisabledFileSuffix.Length]
                : current;
            File.Move(current, target);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail($"{(disable ? "禁用" : "启用")}启动项“{item.Name}”失败：{ex.Message}");
        }
    }

    private static string ResolvePublisher(string command)
    {
        try
        {
            var exePath = ExtractExecutablePath(command);
            if (exePath is null || !File.Exists(exePath))
            {
                return string.Empty;
            }

            var info = FileVersionInfo.GetVersionInfo(exePath);
            return info.CompanyName ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return string.Empty;
        }
    }

    private static string? ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : null;
        }

        var space = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return space > 0 ? trimmed[..(space + 4)] : trimmed;
    }
}
