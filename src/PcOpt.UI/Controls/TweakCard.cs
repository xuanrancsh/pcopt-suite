// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcOpt.Core.Tweak;

namespace PcOpt.UI.Controls;

/// <summary>
/// 产品 B 的 tweak 卡片（PRD 5.6 P0）：风险标签 + 人话名 + 说明 + 应用/还原按钮。
/// 已应用卡片绿色左边框 + ✓（PRD 5.4 生效计数）；按钮文案 = 动作结果（PRD 5.3）。
/// 交互：命令（ApplyCommand/RevertCommand）与路由事件（ApplyClick/RevertClick）双通道，
/// 便于 MVVM 与代码后置两种用法。
/// </summary>
public class TweakCard : Control
{
    /// <summary>标题依赖属性。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TweakCard), new PropertyMetadata(string.Empty));

    /// <summary>说明依赖属性。</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(TweakCard), new PropertyMetadata(string.Empty));

    /// <summary>风险等级依赖属性。</summary>
    public static readonly DependencyProperty RiskProperty = DependencyProperty.Register(
        nameof(Risk), typeof(RiskLevel), typeof(TweakCard), new PropertyMetadata(RiskLevel.Safe));

    /// <summary>是否已应用依赖属性（驱动按钮文案与绿色边框）。</summary>
    public static readonly DependencyProperty IsAppliedProperty = DependencyProperty.Register(
        nameof(IsApplied), typeof(bool), typeof(TweakCard), new PropertyMetadata(false));

    /// <summary>应用命令。</summary>
    public static readonly DependencyProperty ApplyCommandProperty = DependencyProperty.Register(
        nameof(ApplyCommand), typeof(ICommand), typeof(TweakCard), new PropertyMetadata(null));

    /// <summary>还原命令。</summary>
    public static readonly DependencyProperty RevertCommandProperty = DependencyProperty.Register(
        nameof(RevertCommand), typeof(ICommand), typeof(TweakCard), new PropertyMetadata(null));

    /// <summary>应用点击路由事件（命令之外的事件通道）。</summary>
    public static readonly RoutedEvent ApplyClickEvent = EventManager.RegisterRoutedEvent(
        nameof(ApplyClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TweakCard));

    /// <summary>还原点击路由事件。</summary>
    public static readonly RoutedEvent RevertClickEvent = EventManager.RegisterRoutedEvent(
        nameof(RevertClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TweakCard));

    private Button? _actionButton;

    static TweakCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TweakCard), new FrameworkPropertyMetadata(typeof(TweakCard)));
    }

    /// <summary>人话名（动词+结果）。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>人话说明。</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>风险等级（Safe / Notice / Danger，驱动 RiskBadge 颜色）。</summary>
    public RiskLevel Risk
    {
        get => (RiskLevel)GetValue(RiskProperty);
        set => SetValue(RiskProperty, value);
    }

    /// <summary>是否已应用。</summary>
    public bool IsApplied
    {
        get => (bool)GetValue(IsAppliedProperty);
        set => SetValue(IsAppliedProperty, value);
    }

    /// <summary>应用命令。</summary>
    public ICommand? ApplyCommand
    {
        get => (ICommand?)GetValue(ApplyCommandProperty);
        set => SetValue(ApplyCommandProperty, value);
    }

    /// <summary>还原命令。</summary>
    public ICommand? RevertCommand
    {
        get => (ICommand?)GetValue(RevertCommandProperty);
        set => SetValue(RevertCommandProperty, value);
    }

    /// <summary>应用点击事件。</summary>
    public event RoutedEventHandler ApplyClick
    {
        add => AddHandler(ApplyClickEvent, value);
        remove => RemoveHandler(ApplyClickEvent, value);
    }

    /// <summary>还原点击事件。</summary>
    public event RoutedEventHandler RevertClick
    {
        add => AddHandler(RevertClickEvent, value);
        remove => RemoveHandler(RevertClickEvent, value);
    }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_actionButton is not null)
        {
            _actionButton.Click -= OnActionButtonClick;
        }

        _actionButton = GetTemplateChild("PART_ActionButton") as Button;
        if (_actionButton is not null)
        {
            _actionButton.Click += OnActionButtonClick;
        }
    }

    private void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        var command = IsApplied ? RevertCommand : ApplyCommand;
        var routedEvent = IsApplied ? RevertClickEvent : ApplyClickEvent;

        // 命令优先：命令可执行时执行命令；事件通道始终触发（两种用法兼容）
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }

        RaiseEvent(new RoutedEventArgs(routedEvent, this));
    }
}
