// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PcOpt.Products.FrameLab.Converters;

/// <summary>
/// 反相布尔 → 可见性转换器（true=Collapsed、false=Visible）。
/// 用于"PresntMon 不可用提示条"等"条件为假时显示"的场景，与内置 <see cref="BooleanToVisibilityConverter"/> 互补。
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}

/// <summary>反相布尔转换器（true→false、false→true）。用于"忙碌时禁用控件"等绑定。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not bool b || !b;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not bool b || !b;
    }
}
