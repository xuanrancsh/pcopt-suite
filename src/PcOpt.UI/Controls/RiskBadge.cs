// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using System.Windows.Controls;
using PcOpt.Core.Tweak;

namespace PcOpt.UI.Controls;

/// <summary>
/// 三级风险标签（PRD 5.3 / 5.6 P0 组件）。
/// 颜色规范见 PRD 5.2 设计 Token：安全=绿 #22C55E、注意=琥珀 #F59E0B、危险=红 #EF4444。
/// 文案统一走资源文件（架构 8.6）；骨架阶段先内置中文标签，T11 接入 Strings.zh-CN.xaml。
/// </summary>
public class RiskBadge : Control
{
    /// <summary>风险等级依赖属性。</summary>
    public static readonly DependencyProperty RiskProperty = DependencyProperty.Register(
        nameof(Risk),
        typeof(RiskLevel),
        typeof(RiskBadge),
        new PropertyMetadata(RiskLevel.Safe, OnRiskChanged));

    private TextBlock? _label;

    static RiskBadge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RiskBadge), new FrameworkPropertyMetadata(typeof(RiskBadge)));
    }

    /// <summary>风险等级（Safe / Notice / Danger）。</summary>
    public RiskLevel Risk
    {
        get => (RiskLevel)GetValue(RiskProperty);
        set => SetValue(RiskProperty, value);
    }

    /// <summary>与当前 <see cref="Risk"/> 对应的中文标签（随 Risk 变化自动刷新）。</summary>
    public string RiskText => Risk switch
    {
        RiskLevel.Safe => "安全",
        RiskLevel.Notice => "注意",
        RiskLevel.Danger => "危险",
        _ => "未知",
    };

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _label = GetTemplateChild("PART_Label") as TextBlock;
        UpdateLabel();
    }

    private static void OnRiskChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((RiskBadge)sender).UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (_label is not null)
        {
            _label.Text = RiskText;
        }
    }
}
