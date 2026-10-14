// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace PcOpt.UI.Controls;

/// <summary>指标对比结论（帧时间等"越小越好"与"越大越好"指标由调用方给出结论）。</summary>
public enum MetricVerdict
{
    /// <summary>持平。</summary>
    Same,

    /// <summary>变好（绿色）。</summary>
    Better,

    /// <summary>变差（红色）。</summary>
    Worse,
}

/// <summary>一行指标对比数据。</summary>
/// <param name="Name">指标人话名（如"平均帧时间"）。</param>
/// <param name="BeforeText">优化前展示值。</param>
/// <param name="AfterText">优化后展示值。</param>
/// <param name="DeltaText">变化量展示（如"-1.2 ms"）。</param>
/// <param name="Verdict">结论（决定颜色）。</param>
public sealed record MetricComparison(
    string Name,
    string BeforeText,
    string AfterText,
    string DeltaText,
    MetricVerdict Verdict);

/// <summary>
/// 产品 C 的前后对比表（PRD 5.6 P0）：指标 / 优化前 / 优化后 / 变化（改善=绿、变差=红）。
/// 数据经 <see cref="Rows"/> 注入；双主题配色随 Token 切换。
/// </summary>
public class MetricCompareTable : Control
{
    /// <summary>数据行集合依赖属性。</summary>
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows),
        typeof(IEnumerable),
        typeof(MetricCompareTable),
        new PropertyMetadata(null, OnRowsChanged));

    private ItemsControl? _rowsHost;

    static MetricCompareTable()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MetricCompareTable), new FrameworkPropertyMetadata(typeof(MetricCompareTable)));
    }

    /// <summary>数据行集合（<see cref="MetricComparison"/>）。</summary>
    public IEnumerable? Rows
    {
        get => (IEnumerable?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _rowsHost = GetTemplateChild("PART_Rows") as ItemsControl;
        if (_rowsHost is not null)
        {
            _rowsHost.ItemsSource = Rows;
        }
    }

    private static void OnRowsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var table = (MetricCompareTable)sender;
        if (table._rowsHost is not null)
        {
            table._rowsHost.ItemsSource = args.NewValue as IEnumerable;
        }
    }
}
