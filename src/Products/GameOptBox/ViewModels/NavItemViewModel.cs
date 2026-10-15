// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PcOpt.Products.GameOptBox.ViewModels;

/// <summary>左侧导航项（T20）。</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    /// <summary>初始化导航项。</summary>
    /// <param name="title">标题。</param>
    /// <param name="icon">图标（emoji）。</param>
    /// <param name="page">对应模块的视图模型。</param>
    public NavItemViewModel(string title, string icon, object page)
    {
        Title = title;
        Icon = icon;
        Page = page;
    }

    /// <summary>标题。</summary>
    public string Title { get; }

    /// <summary>图标。</summary>
    public string Icon { get; }

    /// <summary>对应模块的视图模型（内容区数据上下文）。</summary>
    public object Page { get; }

    /// <summary>徽标数字（如"已生效 N 项"）。</summary>
    [ObservableProperty]
    private string _badge = string.Empty;

    /// <summary>徽标可见性。</summary>
    [ObservableProperty]
    private Visibility _badgeVisibility = Visibility.Collapsed;

    /// <summary>设置徽标（数量为 0 时隐藏）。</summary>
    public void SetBadge(int count)
    {
        Badge = count.ToString();
        BadgeVisibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
