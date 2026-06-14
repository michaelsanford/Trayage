using Trayage.Core.Configuration;
using Trayage.Core.Models;

namespace Trayage.Core.Notifications;

/// <summary>
/// Decides which newly-arrived inbox items deserve a Windows toast, based on the user's
/// per-class toggles and watched-repository list. Pure and side-effect free.
/// </summary>
public sealed class NotificationRuleEngine
{
    public IReadOnlyList<InboxItem> SelectNotifiable(
        IEnumerable<InboxItem> newItems,
        NotificationSettings settings,
        IReadOnlyCollection<string> watchedRepositories)
    {
        ArgumentNullException.ThrowIfNull(newItems);
        ArgumentNullException.ThrowIfNull(settings);

        var watched = new HashSet<string>(watchedRepositories ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        return newItems.Where(item => ShouldNotify(item, settings, watched)).ToList();
    }

    private static bool ShouldNotify(InboxItem item, NotificationSettings settings, HashSet<string> watched)
    {
        // Only unread activity is worth a toast — the inbox also carries already-read items
        // so the flyout can mirror the full notifications feed.
        if (!item.IsUnread)
        {
            return false;
        }

        // "All activity on this repo": any item from a watched repo toasts when enabled,
        // regardless of its kind.
        if (settings.WatchedRepoActivity && watched.Contains(item.RepositoryFullName))
        {
            return true;
        }

        // Generic repo activity outside a watched repo is intentionally quiet.
        if (item.Kind == InboxItemKind.RepoActivity)
        {
            return false;
        }

        // Otherwise honour the per-class toggle (review requests, mentions, CI, …).
        return settings.IsKindEnabled(item.Kind);
    }
}
