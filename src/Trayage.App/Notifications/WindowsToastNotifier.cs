using CommunityToolkit.WinUI.Notifications;
using Trayage.Core.Models;
using Trayage.Core.Notifications;

namespace Trayage.App.Notifications;

/// <summary>
/// Raises native Windows toasts via the Community Toolkit's <c>ToastContentBuilder</c>,
/// which handles COM activation for unpackaged desktop apps. The item's web URL travels
/// in the toast arguments so a click can open the right page (see <see cref="App"/>).
/// </summary>
public sealed class WindowsToastNotifier : IToastNotifier
{
    public const string ActionArgumentKey = "action";
    public const string OpenAction = "open";
    public const string UrlArgumentKey = "url";

    public void Show(InboxItem item)
    {
        new ToastContentBuilder()
            .AddArgument(ActionArgumentKey, OpenAction)
            .AddArgument(UrlArgumentKey, item.WebUrl)
            .AddText(Headline(item))
            .AddText(item.Title)
            .AddText(item.RepositoryFullName)
            .Show();
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
