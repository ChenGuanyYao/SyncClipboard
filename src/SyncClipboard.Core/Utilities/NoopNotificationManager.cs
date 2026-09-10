using NativeNotification.Interface;

namespace SyncClipboard.Core.Utilities;

/// <summary>
/// 无系统通知能力时的降级实现。核心同步逻辑大量依赖通知接口传递状态，
/// 因此这里保留接口调用链但不向系统投递通知，避免非原生平台入口启动失败。
/// </summary>
internal sealed class NoopNotificationManager : INotificationManager
{
    public bool IsAppLaunchedByNotification => false;
    public INotification Shared { get; } = new NoopNotification();

    public event Action<NotificationActivatedEventArgs>? ActionActivated
    {
        add { }
        remove { }
    }

    public INotification Create() => new NoopNotification();

    public IProgressNotification CreateProgress() => new NoopProgressNotification();

    public IProgressNotification CreateProgress(bool indeterminate) => new NoopProgressNotification
    {
        IsIndeterminate = indeterminate
    };

    public IEnumerable<INotification> GetAllNotifications() => [];

    public void RomoveAllNotifications()
    {
    }

    public INotification Show(string title, string message) => Show(title, message, null);

    public INotification Show(string title, string message, IEnumerable<ActionButton>? buttons)
    {
        var notification = Create();
        notification.Title = title;
        notification.Message = message;
        notification.Buttons = buttons?.ToList() ?? [];
        notification.Show(new NotificationDeliverOption());
        return notification;
    }
}

internal class NoopNotification : INotification
{
    public string? Title { get; set; } = string.Empty;
    public string? Message { get; set; } = string.Empty;
    public Uri? Image { get; set; }
    public List<ActionButton> Buttons { get; set; } = [];
    public Action? ContentAction { get; set; }
    public string NotificationId { get; } = Guid.NewGuid().ToString();
    public bool IsAlive { get; private set; }
    public bool IsCreatedByCurrentProcess => true;

    public void Show(NotificationDeliverOption? options = null)
    {
        IsAlive = true;
    }

    public bool Update() => IsAlive;

    public void Remove()
    {
        IsAlive = false;
    }
}

internal sealed class NoopProgressNotification : NoopNotification, IProgressNotification
{
    public string? ProgressTitle { get; set; } = string.Empty;
    public double? ProgressValue { get; set; }
    public string? ProgressValueTip { get; set; } = string.Empty;
    public bool IsIndeterminate { get; set; }
}
