using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Remembers which inbox items the user marked read in Trayage.
///
/// This exists because a read mark can't rely on the provider. Bitbucket has no notification
/// inbox to mark at all, so a local record is the only thing that can hold the state; GitHub
/// and GitLab are told as well, but that round-trip is best-effort and only visible after the
/// next poll. Applying this overlay to every snapshot is what makes a mark take effect
/// immediately, survive a restart, and work offline.
/// </summary>
public interface IReadStateStore
{
    /// <summary>Absolute path of the backing file (used in logs).</summary>
    string FilePath { get; }

    /// <summary>
    /// Records these items as read, at the <see cref="InboxItem.UpdatedAt"/> they carry now, so
    /// later activity on the same thread can be told apart from the state we just marked.
    /// </summary>
    void MarkRead(IEnumerable<InboxItem> items);

    /// <summary>
    /// Returns <paramref name="items"/> with the overlay applied: an item marked read at or
    /// after its current <see cref="InboxItem.UpdatedAt"/> comes back read and
    /// <see cref="InboxItem.IsExplicitlyRead"/>; one whose thread has moved on since the mark
    /// comes back untouched, and its record is dropped.
    ///
    /// Also prunes records for items that have not been seen for a long time, so the file can't
    /// grow without bound. Persists only if something actually changed.
    /// </summary>
    IReadOnlyList<InboxItem> Apply(IReadOnlyList<InboxItem> items);
}
