// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;
using PcOpt.UI.Controls;

namespace PcOpt.Products.GameOptBox.Services;

/// <summary>
/// 危险操作确认服务抽象（PRD 5.5）：ViewModel 通过它弹出统一二次确认框。
/// 抽象出来便于单元测试替换为记录型替身。
/// </summary>
public interface IDialogService
{
    /// <summary>弹出危险操作二次确认框（模态）。</summary>
    /// <param name="title">标题（人话）。</param>
    /// <param name="message">操作说明。</param>
    /// <param name="consequences">后果列表（≤3 条）。</param>
    /// <param name="requireTypedConfirmation">高危模式：必须输入"我了解风险"。</param>
    ConfirmDialogResult Confirm(
        string title,
        string message,
        IReadOnlyList<string>? consequences = null,
        bool requireTypedConfirmation = false);
}

/// <summary>危险操作确认服务默认实现（复用组件库 <see cref="ConfirmDialog"/>）。</summary>
public sealed class DialogService : IDialogService
{
    /// <inheritdoc/>
    public ConfirmDialogResult Confirm(
        string title,
        string message,
        IReadOnlyList<string>? consequences = null,
        bool requireTypedConfirmation = false)
    {
        var application = Application.Current;
        if (application is null)
        {
            // 无 UI 宿主（如单元测试）：按"未确认"保守处理
            return new ConfirmDialogResult(false, false);
        }

        return application.Dispatcher.CheckAccess()
            ? ConfirmDialog.Ask(application.MainWindow, title, message, consequences, requireTypedConfirmation)
            : application.Dispatcher.Invoke(() =>
                ConfirmDialog.Ask(application.MainWindow, title, message, consequences, requireTypedConfirmation));
    }
}
