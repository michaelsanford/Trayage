using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Trayage.Core.Models;
using Trayage.Core.Notifications;

namespace Trayage.App.Notifications;

/// <summary>
/// Raises native Windows toasts via the Windows App SDK App Notifications API
/// (<see cref="AppNotificationManager"/>). The item's web URL travels in the toast
/// arguments so a click can open the right page (handled in <see cref="App"/>).
/// </summary>
/// <remarks>
/// App notifications depend on the Windows App SDK <em>Singleton</em> package, which is not
/// part of a purely self-contained (xcopy) deployment. We therefore gate on
/// <see cref="AppNotificationManager.IsSupported"/> and silently no-op when the platform
/// can't deliver them, so the tray app never throws on a machine without that runtime.
/// </remarks>
public sealed class WindowsToastNotifier : IToastNotifier
{
    public const string ActionArgumentKey = "action";
    public const string OpenAction = "open";
    public const string UrlArgumentKey = "url";

    private readonly ILogger<WindowsToastNotifier> _logger;

    public WindowsToastNotifier(ILogger<WindowsToastNotifier> logger) => _logger = logger;

    public void Show(InboxItem item)
    {
        if (!AppNotificationManager.IsSupported())
        {
            _logger.LogDebug("App notifications aren't supported on this system; skipping toast.");
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddArgument(ActionArgumentKey, OpenAction)
                .AddArgument(UrlArgumentKey, item.WebUrl)
                .AddText(Headline(item))
                .AddText(item.Title)
                .AddText(item.RepositoryFullName)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            // A failed toast must never take down the tray app.
            _logger.LogWarning(ex, "Failed to show an app notification.");
        }
    }

    private static string Headline(InboxItem item) => item.Kind switch
    {
        InboxItemKind.ReviewRequest => "Review requested",
        InboxItemKind.Mention => "You were mentioned",
        InboxItemKind.Assignment => "Assigned to you",
        InboxItemKind.CiStatus => "CI status changed",
        InboxItemKind.RepoActivity => "New activity",
        _ => "Trayage",
    };
}
