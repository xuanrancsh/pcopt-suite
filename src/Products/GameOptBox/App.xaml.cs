// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Config;
using PcOpt.Core.Journal;
using PcOpt.Core.Native;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.GameOptBox.Services;
using PcOpt.Products.GameOptBox.ViewModels;
using PcOpt.UI.Services;
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.Products.GameOptBox;

/// <summary>
/// 产品 B 应用入口（T20）：单实例互斥 + DI 容器 + 主题 + 签名规则包加载 +
/// 崩溃恢复 + 主窗口（左侧导航 / 首页健康分）。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\pcopt.gameoptbox.singleton";

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

        // 主题：跟随系统（PRD B-P0-11）
        ThemeService.Apply(PcOptThemeMode.System);

        var logger = Services.GetRequiredService<ILogger<App>>();

        // 加载官方签名规则包（先验签，失败拒绝；合规红线 #9 由引擎在加载时校验）
        var package = Services.GetRequiredService<RuleRepository>().LoadPackage();
        if (package.IsSuccess)
        {
            logger.LogInformation("规则包加载完成：成功 {Ok} 条，失败 {Fail} 条", package.Value!.Loaded, package.Value.Failed);
        }
        else
        {
            logger.LogWarning("规则包加载失败：{Error}", package.Error);
        }

        // 开机恢复（内核 RecoveryService：崩溃 / 断电后 LIFO 恢复未闭合会话）
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

        // ---------- 内核基础 ----------
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

        var publicKeyPath = ResourcePaths.FindPublicKey();
        var verifier = publicKeyPath is null ? null : RuleSignatureVerifier.CreateFromFile(publicKeyPath).Value;

        services.AddSingleton<TweakEngine>(sp => new TweakEngine(
            sp.GetServices<ITweakActionHandler>(),
            sp.GetRequiredService<JournalStore>(),
            sp.GetRequiredService<ISystemEnvironment>(),
            sp.GetRequiredService<ILogger<TweakEngine>>(),
            verifier));

        services.AddSingleton<IActionRestorer, TweakEngineActionRestorer>();
        services.AddSingleton<RecoveryService>(sp => new RecoveryService(
            sp.GetRequiredService<JournalStore>(),
            sp.GetRequiredService<IActionRestorer>(),
            sp.GetRequiredService<ILogger<RecoveryService>>()));

        // ---------- 产品 B 服务 ----------
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<RuleRepository>(sp => new RuleRepository(
            sp.GetRequiredService<TweakEngine>(),
            ResourcePaths.FindTweaksDirectory() ?? string.Empty,
            publicKeyPath,
            sp.GetRequiredService<ILogger<RuleRepository>>()));

        services.AddSingleton<HealthScoreService>();
        services.AddSingleton<RuleActionCoordinator>();
        services.AddSingleton<IpcProbeService>();
        services.AddSingleton<LauncherService>();
        services.AddSingleton<IRestorePointService, WindowsRestorePointService>();
        services.AddSingleton<RestoreCenterService>(sp => new RestoreCenterService(
            sp.GetRequiredService<TweakEngine>(),
            sp.GetRequiredService<RuleRepository>(),
            sp.GetRequiredService<JournalStore>(),
            sp.GetRequiredService<IRestorePointService>(),
            sp.GetRequiredService<ILogger<RestoreCenterService>>()));

        services.AddSingleton<ServiceCatalogService>(sp => new ServiceCatalogService(
            sp.GetRequiredService<ServiceControl>(),
            sp.GetRequiredService<ILogger<ServiceCatalogService>>()));
        services.AddSingleton<StartupItemService>(sp => new StartupItemService(
            sp.GetRequiredService<ScheduledTaskController>(),
            sp.GetRequiredService<ILogger<StartupItemService>>()));
        services.AddSingleton<AppxPackageService>(sp => new AppxPackageService(
            sp.GetRequiredService<ILogger<AppxPackageService>>()));
        services.AddSingleton<JunkCleanupService>(sp => new JunkCleanupService(
            sp.GetRequiredService<ILogger<JunkCleanupService>>()));
        services.AddSingleton<IMemoryManager, WindowsMemoryManager>();
        services.AddSingleton<PowerPlanService>(sp => new PowerPlanService(
            sp.GetRequiredService<PowerCfgController>(),
            sp.GetRequiredService<ILogger<PowerPlanService>>()));
        services.AddSingleton<GpuTuningService>(sp => new GpuTuningService(
            sp.GetRequiredService<ISystemEnvironment>(),
            sp.GetRequiredService<ILogger<GpuTuningService>>()));
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<UserConfigService>();

        // ---------- ViewModel / View ----------
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<RuleBrowserViewModel>();
        services.AddSingleton<ServiceOptimizationViewModel>();
        services.AddSingleton<StartupItemsViewModel>();
        services.AddSingleton<LatencyViewModel>();
        services.AddSingleton<PowerPlanViewModel>();
        services.AddSingleton<GpuTuningViewModel>();
        services.AddSingleton<MemoryViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<RestoreCenterViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
