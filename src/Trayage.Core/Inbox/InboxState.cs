using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Holds the latest merged inbox snapshot and notifies listeners when it changes.
/// Acts as the single source of truth the UI binds to and the poller writes into.
/// </summary>
public sealed class InboxState
{
    private volatile IReadOnlyList<InboxItem> _items = Array.Empty<InboxItem>();

    public IReadOnlyList<InboxItem> Items => _items;

    public int UnreadCount => _items.Count(i => i.IsUnread);

    public bool HasUnread => _items.Any(i => i.IsUnread);

    /// <summary>Raised after the snapshot is replaced. May fire on a background thread.</summary>
    public event EventHandler? Changed;

    public void Set(IReadOnlyList<InboxItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
