using Trayage.Core.Models;

namespace Trayage.Core.Notifications;

/// <summary>Shows a native OS notification for an inbox item. Implemented in the UI layer.</summary>
public interface IToastNotifier
{
    void Show(InboxItem item);
}
