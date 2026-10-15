// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using CommunityToolkit.Mvvm.ComponentModel;

namespace PcOpt.Products.FrameLab.ViewModels;

/// <summary>左侧导航项（T31）。</summary>
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
}
