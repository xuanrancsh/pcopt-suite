// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using System.Windows.Input;

namespace PcOpt.UI.Controls;

/// <summary>
/// 空状态组件（PRD 5.4 / 5.6 P0）：插画 + 一句话 + 主行动按钮，不留白。
/// </summary>
public class EmptyState : Control
{
    /// <summary>标题依赖属性。</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata("这里还是空的"));

    /// <summary>说明依赖属性。</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    /// <summary>插画 / 图标（emoji，默认游戏手柄）。</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(EmptyState), new PropertyMetadata("🕹️"));

    /// <summary>主行动按钮文案（空 = 隐藏按钮）。</summary>
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    /// <summary>主行动按钮命令。</summary>
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null));

    static EmptyState()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));
    }

    /// <summary>标题（一句话）。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>补充说明。</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>插画 / 图标。</summary>
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>主行动按钮文案。</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>主行动按钮命令。</summary>
    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
}
