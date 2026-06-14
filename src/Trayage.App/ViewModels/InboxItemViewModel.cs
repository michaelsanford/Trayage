using Trayage.Core.Models;

namespace Trayage.App.ViewModels;

/// <summary>Read-only presentation wrapper around an <see cref="InboxItem"/> for the flyout list.</summary>
public sealed class InboxItemViewModel
{
    public InboxItemViewModel(InboxItem item)
    {
        Item = item;
    }

    public InboxItem Item { get; }

    public string Title => Item.Title;

    public string RepositoryFullName => Item.RepositoryFullName;

    public string WebUrl => Item.WebUrl;

    public string ProviderLabel => Item.Provider == ProviderKind.GitHub ? "GitHub" : "Bitbucket";

    public string KindLabel => Item.Kind switch
    {
        InboxItemKind.ReviewRequest => "Review requested",
        InboxItemKind.Mention => "Mention",
        InboxItemKind.Assignment => "Assigned",
        InboxItemKind.CiStatus => "CI status",
        InboxItemKind.RepoActivity => "Activity",
        _ => Item.Kind.ToString(),
    };

    public string RelativeTime => FormatRelative(Item.UpdatedAt);

    public string Subtitle => $"{RepositoryFullName} · {KindLabel} · {RelativeTime}";

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when.ToUniversalTime();
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalHours < 1)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalDays < 1)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        if (delta.TotalDays < 30)
        {
            return $"{(int)delta.TotalDays}d ago";
        }

        return when.ToLocalTime().ToString("d MMM");
    }
}
