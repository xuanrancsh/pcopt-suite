// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.

// 类型消歧（PcOpt.UI 同时启用 UseWPF + UseWindowsForms，后者使 System.Windows.Forms /
// System.Drawing 进入全局 using，与 WPF 同名类型冲突）。
// 本套件 UI 主体是 WPF；托盘（TrayService）只用 WinForms 的 NotifyIcon 一族与 System.Drawing.Icon。
// 别名优先级高于 using 命名空间，统一在此消歧，各组件文件无需逐个处理。
global using Control = System.Windows.Controls.Control;
global using Button = System.Windows.Controls.Button;
global using UserControl = System.Windows.Controls.UserControl;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Pen = System.Windows.Media.Pen;
global using Point = System.Windows.Point;
global using FlowDirection = System.Windows.FlowDirection;
global using Application = System.Windows.Application;
global using Icon = System.Drawing.Icon;
