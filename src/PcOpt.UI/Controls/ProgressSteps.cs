// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace PcOpt.UI.Controls;

/// <summary>单个步骤的运行时视图模型（内部使用）。</summary>
internal sealed class StepItem
{
    /// <summary>步骤序号（1 起）。</summary>
    public int Index { get; init; }

    /// <summary>步骤人话标签。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>是否为当前步骤。</summary>
    public bool IsCurrent { get; set; }

    /// <summary>是否已完成。</summary>
    public bool IsDone { get; set; }

    /// <summary>圆点内文字：已完成显示 ✓，当前显示序号，未开始显示序号。</summary>
    public string Glyph => IsDone ? "✓" : Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// 多步进度组件（PRD 5.4 / 5.6 P0）：向导 / 会话应用进度。
/// 步骤圆点（当前=蓝、完成=绿✓、未来=灰）+ 当前步骤文案（"正在切换电源计划… 2/6"）。
/// </summary>
public class ProgressSteps : Control
{
    /// <summary>步骤集合依赖属性。</summary>
    public static readonly DependencyProperty StepsProperty = DependencyProperty.Register(
        nameof(Steps),
        typeof(IEnumerable),
        typeof(ProgressSteps),
        new PropertyMetadata(null, OnStepsChanged));

    /// <summary>当前步骤序号（0 起；-1 = 全部未开始）。</summary>
    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex),
        typeof(int),
        typeof(ProgressSteps),
        new PropertyMetadata(0, OnCurrentIndexChanged));

    /// <summary>状态文案依赖属性（显示在步骤行下方）。</summary>
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(ProgressSteps),
        new PropertyMetadata(string.Empty));

    private ItemsControl? _stepsHost;
    private List<StepItem> _items = new();

    static ProgressSteps()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ProgressSteps), new FrameworkPropertyMetadata(typeof(ProgressSteps)));
    }

    /// <summary>步骤标签集合（有序）。</summary>
    public IEnumerable? Steps
    {
        get => (IEnumerable?)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    /// <summary>当前步骤序号（0 起）。</summary>
    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    /// <summary>状态文案。</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _stepsHost = GetTemplateChild("PART_Steps") as ItemsControl;
        RefreshItems();
    }

    private static void OnStepsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((ProgressSteps)sender).RefreshItems();
    }

    private static void OnCurrentIndexChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((ProgressSteps)sender).RefreshItems();
    }

    private void RefreshItems()
    {
        if (_stepsHost is null || Steps is null)
        {
            return;
        }

        var labels = Steps.Cast<object?>().Select(item => item?.ToString() ?? string.Empty).ToArray();
        _items = new List<StepItem>(labels.Length);
        for (var i = 0; i < labels.Length; i++)
        {
            _items.Add(new StepItem
            {
                Index = i + 1,
                Label = labels[i],
                IsCurrent = i == CurrentIndex,
                IsDone = i < CurrentIndex,
            });
        }

        _stepsHost.ItemsSource = _items;
    }
}
