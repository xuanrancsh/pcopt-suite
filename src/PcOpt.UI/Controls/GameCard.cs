// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;

namespace PcOpt.UI.Controls;

/// <summary>
/// 产品 A 的游戏列表卡（PRD 5.6 P0）：游戏名 + 平台徽标 + 反作弊徽标 + 可执行路径 + 运行中标记。
/// </summary>
public class GameCard : Control
{
    /// <summary>游戏名依赖属性。</summary>
    public static readonly DependencyProperty GameNameProperty = DependencyProperty.Register(
        nameof(GameName), typeof(string), typeof(GameCard), new PropertyMetadata(string.Empty));

    /// <summary>平台文案依赖属性（如 "Steam"）。</summary>
    public static readonly DependencyProperty PlatformTextProperty = DependencyProperty.Register(
        nameof(PlatformText), typeof(string), typeof(GameCard), new PropertyMetadata(string.Empty));

    /// <summary>反作弊文案依赖属性（如 "Vanguard（内核级）"；无则 "无反作弊"）。</summary>
    public static readonly DependencyProperty AntiCheatTextProperty = DependencyProperty.Register(
        nameof(AntiCheatText), typeof(string), typeof(GameCard), new PropertyMetadata("无反作弊"));

    /// <summary>可执行路径依赖属性（可空）。</summary>
    public static readonly DependencyProperty ExecutablePathProperty = DependencyProperty.Register(
        nameof(ExecutablePath), typeof(string), typeof(GameCard), new PropertyMetadata(string.Empty));

    /// <summary>是否运行中依赖属性（驱动绿色"● 运行中"标记）。</summary>
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(GameCard), new PropertyMetadata(false));

    static GameCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GameCard), new FrameworkPropertyMetadata(typeof(GameCard)));
    }

    /// <summary>游戏人话名。</summary>
    public string GameName
    {
        get => (string)GetValue(GameNameProperty);
        set => SetValue(GameNameProperty, value);
    }

    /// <summary>平台文案。</summary>
    public string PlatformText
    {
        get => (string)GetValue(PlatformTextProperty);
        set => SetValue(PlatformTextProperty, value);
    }

    /// <summary>反作弊文案。</summary>
    public string AntiCheatText
    {
        get => (string)GetValue(AntiCheatTextProperty);
        set => SetValue(AntiCheatTextProperty, value);
    }

    /// <summary>可执行路径。</summary>
    public string ExecutablePath
    {
        get => (string)GetValue(ExecutablePathProperty);
        set => SetValue(ExecutablePathProperty, value);
    }

    /// <summary>是否运行中。</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }
}
