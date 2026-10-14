// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Windows;

namespace PcOpt.UI.Controls;

/// <summary>危险确认结果（PRD 5.5）。</summary>
/// <param name="Confirmed">用户是否确认继续。</param>
/// <param name="CreateRestorePoint">用户是否勾选"建议先创建还原点"。</param>
public sealed record ConfirmDialogResult(bool Confirmed, bool CreateRestorePoint);

/// <summary>
/// 危险操作二次确认对话框（PRD 5.5 / 5.6 P0）。
/// 交互后置代码（无 MVVM 依赖）：静态 <see cref="Ask"/> 一行调用；
/// 所有危险操作的结果由调用方记入操作日志（可回查，PRD 5.5 第 4 条）。
/// </summary>
public partial class ConfirmDialog : Window
{
    /// <summary>高危确认必须输入的四字（PRD 5.5：防误触）。</summary>
    public const string HighRiskConfirmationText = "我了解风险";

    /// <summary>后果列表最大条数（PRD 5.3 不恐吓原则）。</summary>
    public const int MaxConsequences = 3;

    /// <summary>初始化对话框实例（高级用法；一般用 <see cref="Ask"/>）。</summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>用户是否确认继续。</summary>
    public bool Confirmed { get; private set; }

    /// <summary>用户是否勾选"建议先创建还原点"。</summary>
    public bool CreateRestorePoint => RestorePointCheckBox.IsChecked == true;

    /// <summary>是否为高危模式（需输入"我了解风险"）。</summary>
    public bool RequireTypedConfirmation { get; private set; }

    /// <summary>
    /// 弹出危险确认框并返回结果（模态）。
    /// </summary>
    /// <param name="owner">宿主窗口（居中显示）；可为 null。</param>
    /// <param name="title">标题（人话，说明要确认什么）。</param>
    /// <param name="message">操作说明。</param>
    /// <param name="consequences">后果列表（≤3 条，超出截断；说清后果而非恐吓）。</param>
    /// <param name="requireTypedConfirmation">高危模式：必须输入"我了解风险"才能确认。</param>
    public static ConfirmDialogResult Ask(
        Window? owner,
        string title,
        string message,
        IReadOnlyList<string>? consequences = null,
        bool requireTypedConfirmation = false)
    {
        var dialog = new ConfirmDialog
        {
            Owner = owner,
            RequireTypedConfirmation = requireTypedConfirmation,
        };

        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConsequenceList.ItemsSource = consequences is null
            ? null
            : consequences.Take(MaxConsequences).ToList();
        dialog.TypedConfirmationPanel.Visibility = requireTypedConfirmation
            ? Visibility.Visible
            : Visibility.Collapsed;
        dialog.ConfirmButton.IsEnabled = !requireTypedConfirmation;

        _ = dialog.ShowDialog();
        return new ConfirmDialogResult(dialog.Confirmed, dialog.CreateRestorePoint);
    }

    private void OnConfirmationInputChanged(object sender, RoutedEventArgs e)
    {
        // 高危模式：输入与目标四字完全一致才允许确认（防误触，PRD 5.5）
        ConfirmButton.IsEnabled = string.Equals(
            ConfirmationInput.Text?.Trim(),
            HighRiskConfirmationText,
            StringComparison.Ordinal);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (RequireTypedConfirmation
            && !string.Equals(ConfirmationInput.Text?.Trim(), HighRiskConfirmationText, StringComparison.Ordinal))
        {
            // 双保险：按钮禁用外再拦一次
            return;
        }

        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
