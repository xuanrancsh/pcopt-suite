// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;

namespace PcOpt.UI.Controls;

/// <summary>大状态卡三态（PRD 5.4：全局状态条）。</summary>
public enum HeroStatus
{
    /// <summary>🛡️ 守护中（蓝）。</summary>
    Guarding,

    /// <summary>🎮 游戏中（绿，附游戏名与应用项数）。</summary>
    Playing,

    /// <summary>✅ 已还原（灰，附项数）。</summary>
    Restored,
}

/// <summary>
/// 产品 A 主界面顶部大状态卡（PRD 5.4 / 5.6 P0）。
/// 一眼可见三态：守护中（蓝）/ 游戏中（绿 + 游戏名 + 应用项数）/ 已还原（灰 + 项数）。
/// </summary>
public class StatusHeroCard : Control
{
    /// <summary>状态依赖属性。</summary>
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status),
        typeof(HeroStatus),
        typeof(StatusHeroCard),
        new PropertyMetadata(HeroStatus.Guarding, OnStateChanged));

    /// <summary>游戏名（Playing 态展示）。</summary>
    public static readonly DependencyProperty GameNameProperty = DependencyProperty.Register(
        nameof(GameName),
        typeof(string),
        typeof(StatusHeroCard),
        new PropertyMetadata(string.Empty, OnStateChanged));

    /// <summary>已应用项数（Playing 态展示）。</summary>
    public static readonly DependencyProperty AppliedCountProperty = DependencyProperty.Register(
        nameof(AppliedCount),
        typeof(int),
        typeof(StatusHeroCard),
        new PropertyMetadata(0, OnStateChanged));

    /// <summary>已还原项数（Restored 态展示）。</summary>
    public static readonly DependencyProperty RevertedCountProperty = DependencyProperty.Register(
        nameof(RevertedCount),
        typeof(int),
        typeof(StatusHeroCard),
        new PropertyMetadata(0, OnStateChanged));

    /// <summary>状态人话文案（只读 DP；随 Status / GameName / 计数变化自动刷新）。</summary>
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(StatusText),
        typeof(string),
        typeof(StatusHeroCard),
        new PropertyMetadata("守护中"));

    /// <summary>状态人话文案依赖属性（对 XAML TemplateBinding 暴露）。</summary>
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;

    /// <summary>副文案（只读 DP；随状态数据变化自动刷新）。</summary>
    private static readonly DependencyPropertyKey DetailTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DetailText),
        typeof(string),
        typeof(StatusHeroCard),
        new PropertyMetadata("正在后台守护，检测到游戏启动会自动应用优化"));

    /// <summary>副文案依赖属性（对 XAML TemplateBinding 暴露）。</summary>
    public static readonly DependencyProperty DetailTextProperty = DetailTextPropertyKey.DependencyProperty;

    static StatusHeroCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusHeroCard), new FrameworkPropertyMetadata(typeof(StatusHeroCard)));
    }

    /// <summary>当前三态。</summary>
    public HeroStatus Status
    {
        get => (HeroStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>游戏名。</summary>
    public string GameName
    {
        get => (string)GetValue(GameNameProperty);
        set => SetValue(GameNameProperty, value);
    }

    /// <summary>已应用项数。</summary>
    public int AppliedCount
    {
        get => (int)GetValue(AppliedCountProperty);
        set => SetValue(AppliedCountProperty, value);
    }

    /// <summary>已还原项数。</summary>
    public int RevertedCount
    {
        get => (int)GetValue(RevertedCountProperty);
        set => SetValue(RevertedCountProperty, value);
    }

    /// <summary>状态人话文案（随 Status 变化）。</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        private set => SetValue(StatusTextProperty, value);
    }

    /// <summary>副文案：游戏中显示游戏名与应用项数；已还原显示项数；守护中显示守护说明。</summary>
    public string DetailText
    {
        get => (string)GetValue(DetailTextProperty);
        private set => SetValue(DetailTextProperty, value);
    }

    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (StatusHeroCard)sender;
        card.StatusText = card.Status switch
        {
            HeroStatus.Guarding => "守护中",
            HeroStatus.Playing => "游戏中",
            HeroStatus.Restored => "已还原",
            _ => "未知状态",
        };

        card.DetailText = card.Status switch
        {
            HeroStatus.Playing => $"正在守护 {card.GameName}，已应用 {card.AppliedCount} 项优化",
            HeroStatus.Restored => $"已全部还原，共 {card.RevertedCount} 项，你的电脑恢复正常了",
            HeroStatus.Guarding => "正在后台守护，检测到游戏启动会自动应用优化",
            _ => string.Empty,
        };
    }
}
