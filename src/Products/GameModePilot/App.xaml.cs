// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PcOpt.Core.Config;
using PcOpt.Core.Game;
using PcOpt.Core.Hardware;
using PcOpt.Core.Journal;
using PcOpt.Core.Native;
using PcOpt.Core.Platform;
using PcOpt.Core.Tweak;
using PcOpt.Core.Tweak.Handlers;
using PcOpt.Products.GameModePilot.Services;
using PcOpt.Products.GameModePilot.ViewModels;
using PcOpt.Products.GameModePilot.Views;
using PcOpt.UI.Services;
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.Products.GameModePilot;

/// <summary>
/// 产品 A 应用入口（T12）：单实例互斥 + DI 容器 + 主题 + 开机恢复 + 守护 / IPC 启动 + 首次向导（T16）。
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\pcopt.gamemodepilot.singleton";

    private Mutex? _singleInstanceMutex;

    /// <summary>全局主题服务（各视图共用）。</summary>
    public static PcOptThemeService ThemeService { get; } = new();

    /// <summary>全局 DI 容器。</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：命名 Mutex；已有实例时直接退出（守护程序不应重复常驻）
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        Services = BuildServiceProvider();

        // 主题：跟随系统（PRD 5.2）
        ThemeService.Apply(PcOptThemeMode.System);

        var logger = Services.GetRequiredService<ILogger<App>>();

        // 预注册与具体游戏无关的内置规则：既供 IPC applyProfile 引用，也让开机恢复可还原这些规则
        RegisterStaticRules();

        // 开机恢复（内核 RecoveryService：崩溃 / 断电后 LIFO 恢复未闭合会话）
        var recovery = Services.GetRequiredService<RecoveryService>().RecoverOpenSessions();
        if (!recovery.IsSuccess)
        {
            logger.LogWarning("开机恢复失败：{Error}", recovery.Error);
        }
        else if (recovery.Value!.EntriesRestored > 0)
        {
            logger.LogInformation("开机恢复完成，共还原 {Count} 项", recovery.Value.EntriesRestored);
        }

        // 启动守护（ETW 监控，权限不足时降级为不可自动检测）与 IPC 服务端
        var guardian = Services.GetRequiredService<GuardianService>();
        var guardianStart = guardian.Start();
        if (!guardianStart.IsSuccess)
        {
            logger.LogWarning("守护启动降级（进程监控不可用）：{Error}", guardianStart.Error);
        }

        Services.GetRequiredService<IpcServer>().Start();

        // 首次运行向导（T16）
        if (IsFirstRun())
        {
            var wizard = new WizardWindow(Services.GetRequiredService<WizardViewModel>());
            _ = wizard.ShowDialog();

            // 落盘最小用户配置，标记"已完成首次向导"，避免下次启动重复弹出
            var save = Services.GetRequiredService<UserConfigService>()
                .Save(new UserConfig { Product = "GameModePilot" });
            if (!save.IsSuccess)
            {
                logger.LogWarning("首次运行配置写入失败：{Error}", save.Error);
            }
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (recovery.IsSuccess && recovery.Value!.EntriesRestored > 0)
        {
            Services.GetRequiredService<ProductTrayService>()
                .Notify("已恢复上次的改动", $"检测到上次未正常退出，已自动还原 {recovery.Value.EntriesRestored} 项。");
        }
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        (Services as IDisposable)?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static bool IsFirstRun()
    {
        var configPath = Services.GetRequiredService<UserConfigService>().GetConfigFilePath("GameModePilot");
        return !File.Exists(configPath);
    }

    private static void RegisterStaticRules()
    {
        var engine = Services.GetRequiredService<TweakEngine>();
        engine.RegisterRule(RuleSetBuilder.BuildPowerRule());
        engine.RegisterRule(RuleSetBuilder.BuildTimerRule());
        engine.RegisterRule(RuleSetBuilder.BuildBackgroundPriorityRule());
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

        // ---------- 反作弊 / 游戏 ----------
        services.AddSingleton<ISystemSecuritySnapshot, WindowsSecuritySnapshot>();
        services.AddSingleton<IAntiCheatDetector, AntiCheatDetector>();
        services.AddSingleton<IGameSourcePaths, WindowsGameSourcePaths>();
        services.AddSingleton<IUninstallRegistryReader, WindowsUninstallRegistryReader>();
        services.AddSingleton<GameLibraryScanner>();
        services.AddSingleton<IProcessMatcher>(_ =>
        {
            var path = ResourcePaths.FindGamesDatabase();
            if (path is null)
            {
                return new NullProcessMatcher();
            }

            var database = GameDatabase.LoadFromFile(path);
            return database.IsSuccess && database.Value is not null
                ? new GameProcessMatcher(database.Value)
                : new NullProcessMatcher();
        });
        services.AddSingleton<GameProcessWatcher>();
        services.AddSingleton<GameWatcher>();

        // ---------- 产品 A 服务 ----------
        services.AddSingleton<SessionHistoryStore>(sp => new SessionHistoryStore(
            sp.GetRequiredService<ILogger<SessionHistoryStore>>()));
        services.AddSingleton<TweakPreferences>();
        services.AddSingleton<SessionEngine>(sp => new SessionEngine(
            sp.GetRequiredService<TweakEngine>(),
            sp.GetRequiredService<IAntiCheatDetector>(),
            sp.GetRequiredService<JournalStore>(),
            sp.GetRequiredService<SessionHistoryStore>(),
            sp.GetRequiredService<ILogger<SessionEngine>>()));
        services.AddSingleton<GuardianService>();
        services.AddSingleton<IIpcCommandHandler>(sp => sp.GetRequiredService<GuardianService>());
        services.AddSingleton<IpcServer>();
        services.AddSingleton<AutoStartService>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<ProductTrayService>();
        services.AddSingleton<UserConfigService>();

        // ---------- ViewModel / View ----------
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<GamesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<WizardViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
