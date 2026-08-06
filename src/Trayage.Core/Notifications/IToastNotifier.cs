using Trayage.Core.Models;

namespace Trayage.Core.Notifications;

/// <summary>Shows a native OS notification for an inbox item. Implemented in the UI layer.</summary>
public interface IToastNotifier
{
    /// <summary>
    /// Whether the platform can actually deliver notifications. May be false on a
    /// self-contained install whose machine lacks the required OS notification runtime;
    /// callers should degrade gracefully and surface guidance rather than fail.
    /// </summary>
    bool IsAvailable { get; }

    void Show(InboxItem item);

    /// <summary>
    /// Shows a free-form notification not tied to an inbox item — used to surface app health
    /// (e.g. a provider whose sync started failing). <paramref name="url"/>, when non-null,
    /// is opened if the user clicks the toast.
    /// </summary>
    void ShowMessage(string title, string body, string? url = null);
}
