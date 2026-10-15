// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using PcOpt.Products.GameModePilot.ViewModels;

namespace PcOpt.Products.GameModePilot.Views;

/// <summary>
/// 首次运行向导窗口（T16，PRD A-P0-7）：3 步，可跳过、可回退。
/// </summary>
public partial class WizardWindow : FluentWindow
{
    private readonly WizardViewModel _viewModel;

    /// <summary>初始化向导窗口。</summary>
    public WizardWindow(WizardViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        _viewModel.Completed += OnCompleted;
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        _viewModel.Completed -= OnCompleted;
        DialogResult = true;
        Close();
    }
}
