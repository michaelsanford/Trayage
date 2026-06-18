using Trayage.Core.Configuration;
using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Decides whether a read item is recent enough to still surface, bridging GitHub's
/// web-vs-REST read-state desync. The window defaults to 2× the poll interval and is
/// gated by <see cref="TrayageSettings.SurfaceRecentlyModified"/>; the single source of
/// truth for both the flyout and the notification rule engine.
/// </summary>
public static class InboxRecency
{
    /// <summary>The recency window, or <c>null</c> when the feature is switched off.</summary>
    public static TimeSpan? WindowFor(TrayageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.SurfaceRecentlyModified
            ? TimeSpan.FromSeconds(Math.Max(settings.PollIntervalSeconds, 1) * 2.0)
            : null;
    }

    /// <summary>True when <paramref name="window"/> is set and the item was updated within it.</summary>
    public static bool IsRecent(InboxItem item, DateTimeOffset now, TimeSpan? window)
    {
        ArgumentNullException.ThrowIfNull(item);

        return window is { } w && now - item.UpdatedAt <= w;
    }
}
