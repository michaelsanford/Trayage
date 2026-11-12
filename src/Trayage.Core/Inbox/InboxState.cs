using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Holds the latest merged inbox snapshot and notifies listeners when it changes.
/// Acts as the single source of truth the UI binds to and the poller writes into.
/// </summary>
public sealed class InboxState
{
    // Items and their unread count swap together, so readers never see one without the other.
    private volatile Snapshot _snapshot = new(Array.Empty<InboxItem>(), 0);

    public IReadOnlyList<InboxItem> Items => _snapshot.Items;

    /// <summary>Counted once per snapshot; the tray reads it on every change.</summary>
    public int UnreadCount => _snapshot.UnreadCount;

    public bool HasUnread => _snapshot.UnreadCount > 0;

    /// <summary>Raised after the snapshot is replaced. May fire on a background thread.</summary>
    public event EventHandler? Changed;

    public void Set(IReadOnlyList<InboxItem>? items)
    {
        var snapshot = items ?? Array.Empty<InboxItem>();
        _snapshot = new Snapshot(snapshot, snapshot.Count(i => i.IsUnread));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Snapshot(IReadOnlyList<InboxItem> Items, int UnreadCount);
}
