// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using System.Windows.Input;
using PcOpt.UI.Controls;
using PcOpt.UI.Services;
// .NET 9 起 Window 自带 ThemeMode 实例属性会遮蔽同名枚举类型，用别名消歧
using PcOptThemeMode = PcOpt.UI.Services.ThemeMode;

namespace PcOpt.UI.Demo;

/// <summary>
/// 走查窗口代码后置：驱动全部 P0 组件的交互演示（PRD 5.6）。
/// </summary>
public partial class MainWindow : Window
{
    private readonly TrayService _tray;

    /// <summary>初始化窗口与托盘。</summary>
    public MainWindow()
    {
        InitializeComponent();

        // 初始数据：英雄卡守护中；步骤与指标表填充示例数据
        HeroCard.Status = HeroStatus.Guarding;
        HeroCard.GameName = "无畏契约";
        HeroCard.AppliedCount = 6;
        HeroCard.RevertedCount = 6;

        Steps.Steps = new[]
        {
            "检查反作弊", "切换电源计划", "调整服务", "设置进程优先级", "请求计时器分辨率", "完成校验",
        };
        Steps.CurrentIndex = -1;
        Steps.StatusText = "尚未开始";

        MetricTable.Rows = new[]
        {
            new MetricComparison("平均帧时间", "11.8 ms", "9.6 ms", "-2.2 ms", Controls.MetricVerdict.Better),
            new MetricComparison("1% Low 帧时间", "24.5 ms", "18.2 ms", "-6.3 ms", Controls.MetricVerdict.Better),
            new MetricComparison("帧时间标准差", "4.2 ms", "3.1 ms", "-1.1 ms", Controls.MetricVerdict.Better),
            new MetricComparison("CPU 占用（峰值）", "62 %", "71 %", "+9 %", Controls.MetricVerdict.Worse),
        };

        Chart.FrameTimes = GenerateFrameTimes();
        Chart.AverageMs = 9.6;
        Chart.LowOnePercentMs = 18.2;

        // 托盘服务（三产品共用模式）
        _tray = new TrayService("PcOpt.UI Demo - 走查中");
        _tray.OpenRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(Close);
        _tray.SetMenuItems(new[]
        {
            new TrayMenuItem { Text = "打开主界面", OnClick = () => Dispatcher.Invoke(Show) },
            new TrayMenuItem { Text = "发送测试通知", OnClick = () => Dispatcher.Invoke(() => Toasts.ShowToast("来自托盘的通知")) },
            TrayMenuItem.Separator,
        });

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Mica 背景（Win11 22621+；Win10 自动兜底为纯色背景）
        PcOptThemeService.TryApplyMica(this);
    }

    // ---------- 标题栏 ----------

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ---------- 主题 ----------

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (ThemeSystem.IsChecked == true)
        {
            App.ThemeService.Apply(PcOptThemeMode.System);
        }
        else if (ThemeLight.IsChecked == true)
        {
            App.ThemeService.Apply(PcOptThemeMode.Light);
        }
        else
        {
            App.ThemeService.Apply(PcOptThemeMode.Dark);
        }
    }

    // ---------- StatusHeroCard ----------

    private void OnHeroGuarding(object sender, RoutedEventArgs e) => HeroCard.Status = HeroStatus.Guarding;

    private void OnHeroPlaying(object sender, RoutedEventArgs e) => HeroCard.Status = HeroStatus.Playing;

    private void OnHeroRestored(object sender, RoutedEventArgs e) => HeroCard.Status = HeroStatus.Restored;

    // ---------- TweakCard ----------

    private void OnCardApply(object sender, RoutedEventArgs e)
    {
        if (sender is not TweakCard card)
        {
            return;
        }

        card.IsApplied = true;
        Toasts.ShowToast($"已应用：{card.Title}", () =>
        {
            card.IsApplied = false;
            Toasts.ShowToast("已撤销刚才的应用");
        });
    }

    private void OnCardRevert(object sender, RoutedEventArgs e)
    {
        if (sender is not TweakCard card)
        {
            return;
        }

        card.IsApplied = false;
        Toasts.ShowToast($"已还原：{card.Title}，你的电脑恢复正常了");
    }

    // ---------- ConfirmDialog ----------

    private void OnConfirmNormal(object sender, RoutedEventArgs e)
    {
        var result = ConfirmDialog.Ask(
            this,
            "关闭 Windows Defender 实时防护？",
            "该操作会降低系统对可疑文件的实时扫描能力。",
            new[] { "游戏时后台扫描占用会减少", "新下载文件的防护会变弱", "之后可以随时在这里重新开启" });

        Toasts.ShowToast(result.Confirmed
            ? $"已确认执行（创建还原点：{(result.CreateRestorePoint ? "是" : "否")}）"
            : "已取消，什么都没有改动");
    }

    private void OnConfirmHighRisk(object sender, RoutedEventArgs e)
    {
        var result = ConfirmDialog.Ask(
            this,
            "关闭 Windows 自动更新？",
            "这是高危操作：关闭后系统将不会自动获得安全补丁。",
            new[] { "系统安全补丁将暂停", "部分驱动更新也需要手动检查", "恢复方式：设置 → 更新与安全" },
            requireTypedConfirmation: true);

        Toasts.ShowToast(result.Confirmed ? "已确认执行高危操作" : "已取消高危操作");
    }

    // ---------- ProgressSteps ----------

    private async void OnRunSteps(object sender, RoutedEventArgs e)
    {
        var labels = new[] { "检查反作弊", "切换电源计划", "调整服务", "设置进程优先级", "请求计时器分辨率", "完成校验" };
        for (var i = 0; i < labels.Length; i++)
        {
            Steps.CurrentIndex = i;
            Steps.StatusText = $"正在{labels[i]}… {i + 1}/{labels.Length}";
            await System.Threading.Tasks.Task.Delay(700);
        }

        Steps.StatusText = "会话已应用完成，共 6 项";
        Toasts.ShowToast("优化已应用，退出游戏后会自动还原");
    }

    // ---------- Toast / Tray ----------

    private void OnToastPlain(object sender, RoutedEventArgs e) =>
        Toasts.ShowToast("已全部还原，共 6 项，你的电脑恢复正常了");

    private void OnToastUndo(object sender, RoutedEventArgs e)
    {
        Toasts.ShowToast("已应用：让游戏少抢 CPU 的后台服务", () =>
        {
            Toasts.ShowToast("已撤销刚才的应用");
        });
    }

    private void OnTrayBalloon(object sender, RoutedEventArgs e) =>
        _tray.ShowBalloonTip("PcOpt.UI Demo", "托盘工作正常：双击图标可唤回主界面");

    // ---------- 数据 ----------

    private static double[] GenerateFrameTimes()
    {
        var random = new Random(42);
        var result = new double[240];
        for (var i = 0; i < result.Length; i++)
        {
            // 9.6ms 均值附近 + 周期性抖动 + 少量尖峰（模拟真实帧时间分布）
            var wave = Math.Sin(i / 24.0) * 0.6;
            var spike = random.NextDouble() < 0.02 ? random.NextDouble() * 14 : 0;
            result[i] = 9.6 + wave + (random.NextDouble() - 0.5) * 1.4 + spike;
        }

        return result;
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
    }
}
