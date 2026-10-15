// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;

// 注册本组件库的泛型主题字典（Themes/Generic.xaml）。
// 说明：PcOpt.UI 的自定义控件（StatusHeroCard / TweakCard / GameCard / ToastHost 等，PRD 5.6 P0）
// 通过 DefaultStyleKey 从"泛型主题字典"解析控件模板；WPF 仅在程序集声明 ThemeInfo 且
// 第二参数为 SourceAssembly 时才会加载 themes/generic.xaml。缺失此声明会导致控件无模板（不可见）。
// 查找优先级（与 PcOptThemeService 设计一致）：Application.Resources（主题服务注入的 Palette）
// 优先于本主题字典，从而支持深浅色切换无闪烁。
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]
