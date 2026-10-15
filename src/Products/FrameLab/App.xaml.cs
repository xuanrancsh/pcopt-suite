// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Journal;
using PcOpt.Core.Native;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.FrameLab.Services;
using PcOpt.Products.FrameLab.ViewModels;
using PcOpt.UI.Services;
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.Products.FrameLab;

/// <summary>
/// 产品 C（FrameLab 帧率诊断）应用入口（T31）：
/// 单实例互斥 + DI 容器 + 跟随系统主题 + 崩溃恢复（零残留）+ 主窗口（左侧导航 / 测试台）。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\pcopt.framelab.singleton";

    private Mutex? _singleInstanceMutex;

    /// <summary>全局主题服务（各视图共用）。</summary>
    public static PcOptThemeService ThemeService { get; } = new();

    /// <summary>全局 DI 容器。</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        Services = BuildServiceProvider();

        // 主题：跟随系统（PRD C-P0-7）
        ThemeService.Apply(PcOptThemeMode.System);

        var logger = Services.GetRequiredService<ILogger<App>>();

        // 开机恢复：还原上次未正常关闭时残留的优化（内核零残留承诺 / 架构 4.3）
        var recovery = Services.GetRequiredService<RecoveryService>().RecoverOpenSessions();
        if (recovery.IsSuccess && recovery.Value!.EntriesRestored > 0)
        {
            logger.LogInformation("开机恢复完成，共还原 {Count} 项", recovery.Value.EntriesRestored);
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (recovery.IsSuccess && recovery.Value!.EntriesRestored > 0)
        {
            Services.GetRequiredService<INotificationService>()
                .Notify($"检测到上次未正常退出，已自动还原 {recovery.Value.EntriesRestored} 项改动。");
        }
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(Path.Combine(ResourcePaths.LogDirectory, "app.log")));
        });

        // ---------- 内核基础（最小引擎 / 还原依赖） ----------
        services.AddSingleton<ISystemEnvironment, WindowsSystemEnvironment>();
        services.AddSingleton<JournalStore>(sp => new JournalStore(sp.GetRequiredService<ILogger<JournalStore>>()));

        services.AddSingleton<ServiceControl>();
        services.AddSingleton<PowerCfgController>();
        services.AddSingleton<ProcessPriorityController>();
        services.AddSingleton<ScheduledTaskController>();
        services.AddSingleton<TimerResolutionController>();

        services.AddSingleton<RegistrySetActionHandler>();
        services.AddSingleton<ServiceStartTypeActionHandler>();
        services.AddSingleton<ScheduledTaskActionHandler>();
        services.AddSingleton<PowerCfgActionHandler>();
        services.AddSingleton<ProcessPriorityActionHandler>();
        services.AddSingleton<TimerResolutionActionHandler>();
        services.AddSingleton<ShellActionHandler>();

        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<RegistrySetActionHandler>());
        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<ServiceStartTypeActionHandler>());
        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<ScheduledTaskActionHandler>());
        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<PowerCfgActionHandler>());
        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<ProcessPriorityActionHandler>());
        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<TimerResolutionActionHandler>());
        services.AddSingleton<ITweakActionHandler>(sp => sp.GetRequiredService<ShellActionHandler>());

        services.AddSingleton<TweakEngine>(sp => new TweakEngine(
            sp.GetServices<ITweakActionHandler>(),
            sp.GetRequiredService<JournalStore>(),
            sp.GetRequiredService<ISystemEnvironment>(),
            sp.GetRequiredService<ILogger<TweakEngine>>(),
            signatureVerifier: null));

        services.AddSingleton<IActionRestorer, TweakEngineActionRestorer>();
        services.AddSingleton<RecoveryService>(sp => new RecoveryService(
            sp.GetRequiredService<JournalStore>(),
            sp.GetRequiredService<IActionRestorer>(),
            sp.GetRequiredService<ILogger<RecoveryService>>()));

        // ---------- 产品 C 服务 ----------
        services.AddSingleton<INotificationService, NotificationService>();

        services.AddSingleton<IPresentMonRunner, ProcessPresentMonRunner>();
        services.AddSingleton<IDpcSampler, SchedulingJitterDpcSampler>();
        services.AddSingleton<CaptureService>();

        services.AddSingleton<CaptureSessionStore>();
        services.AddSingleton<HistoryService>(sp => new HistoryService(sp.GetRequiredService<ILogger<HistoryService>>()));
        services.AddSingleton<SettingsService>(sp => new SettingsService(sp.GetRequiredService<ILogger<SettingsService>>()));
        services.AddSingleton<ReportExporter>();

        services.AddSingleton<ISystemSignalProvider, WindowsSystemSignalProvider>();
        services.AddSingleton<IGameModePilotClient, GameModePilotClient>();
        services.AddSingleton<IMemoryCleaner, WindowsMemoryCleaner>();
        services.AddSingleton<MinimalOptimizationEngine>();
        services.AddSingleton<OptimizationLoopService>();

        // ---------- ViewModel / View ----------
        services.AddSingleton<TestBenchViewModel>();
        services.AddSingleton<ReportViewModel>();
        services.AddSingleton<DiagnoseViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
