// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PcOpt.UI.Controls;

/// <summary>单条 Toast 的运行时视图模型（内部使用；构造后不可变，无需 INotifyPropertyChanged）。</summary>
internal sealed class ToastItem
{
    private readonly Action? _undo;
    private readonly DispatcherTimer _timer;

    /// <summary>初始化 Toast（6 秒自动消失）。</summary>
    public ToastItem(string message, Action? undo, Action<ToastItem> closeRequested)
    {
        Message = message;
        _undo = undo;
        UndoCommand = new RelayCommand(_ => Undo(), _ => HasUndo);
        CloseCommand = new RelayCommand(_ => closeRequested(this));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _timer.Tick += (_, _) => closeRequested(this);
        _timer.Start();
    }

    /// <summary>通知正文（人话，PRD 5.3：状态变化主动告知）。</summary>
    public string Message { get; }

    /// <summary>是否带撤销通道。</summary>
    public bool HasUndo => _undo is not null;

    /// <summary>撤销命令。</summary>
    public ICommand UndoCommand { get; }

    /// <summary>关闭命令。</summary>
    public ICommand CloseCommand { get; }

    /// <summary>停止自动消失计时器（手动关闭时调用，避免重复触发）。</summary>
    public void StopTimer() => _timer.Stop();

    private void Undo()
    {
        _timer.Stop();
        _undo?.Invoke();
    }
}

/// <summary>极简命令实现（Toast 内部使用，避免引入 MVVM 依赖）。</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    /// <summary>初始化命令。</summary>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>本命令的可执行条件构造后不变（撤销通道显隐由 Visibility 绑定驱动），无需引发重算。</summary>
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => _execute(parameter);
}

/// <summary>
/// 右下角通知宿主（PRD 5.4 / 5.6 P0）：Toast 通知 + [撤销] 按钮。
/// 用法：放在窗口最上层 Grid 中作为浮层（ HorizontalAlignment=Right / VerticalAlignment=Bottom 已内置），
/// 代码调用 <see cref="ShowToast"/> 推送；6 秒自动消失，支持手动关闭与撤销。
/// </summary>
public class ToastHost : Control
{
    private readonly ObservableCollection<ToastItem> _toasts = new();

    static ToastHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ToastHost), new FrameworkPropertyMetadata(typeof(ToastHost)));
    }

    /// <summary>当前 Toast 数量（测试 / 状态查询用）。</summary>
    public int ToastCount => _toasts.Count;

    /// <summary>
    /// 推送一条 Toast。
    /// </summary>
    /// <param name="message">人话通知（如"已全部还原，共 6 项，你的电脑恢复正常了"）。</param>
    /// <param name="undo">撤销回调；非 null 时显示 [撤销] 按钮（PRD 5.4 撤销通道）。</param>
    public void ShowToast(string message, Action? undo = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var item = new ToastItem(message, undo, CloseToast);
        _toasts.Add(item);
    }

    /// <summary>关闭指定 Toast（宿主窗口卸载时清理用；ToastItem 为内部实现细节，故本方法 internal）。</summary>
    internal void CloseToast(ToastItem item)
    {
        item.StopTimer();
        _ = _toasts.Remove(item);
    }

    /// <summary>关闭全部 Toast。</summary>
    public void Clear() => _toasts.Clear();

    /// <inheritdoc/>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Toasts") is ItemsControl host)
        {
            host.ItemsSource = _toasts;
        }
    }
}
