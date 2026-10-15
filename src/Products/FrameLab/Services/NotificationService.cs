// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
namespace PcOpt.Products.FrameLab.Services;

/// <summary>
/// 通知服务抽象：ViewModel 通过它向宿主窗口的 ToastHost 推送通知（PRD 5.4）。抽出来便于单测替换。
/// </summary>
public interface INotificationService
{
    /// <summary>请求弹出 Toast 时触发（由主窗口承接）。</summary>
    event EventHandler<string>? ToastRequested;

    /// <summary>推送一条人话通知。</summary>
    /// <param name="message">通知正文。</param>
    void Notify(string message);
}

/// <summary>通知服务默认实现（事件总线，不依赖 UI 线程）。</summary>
public sealed class NotificationService : INotificationService
{
    /// <inheritdoc/>
    public event EventHandler<string>? ToastRequested;

    /// <inheritdoc/>
    public void Notify(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ToastRequested?.Invoke(this, message);
    }
}
