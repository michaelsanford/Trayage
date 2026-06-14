using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Compares two inbox snapshots to find genuinely new or freshly-updated items, so
/// notifications fire on real activity rather than on every poll. Pure function.
/// </summary>
public sealed class InboxDiffer
{
    /// <summary>
    /// Returns items in <paramref name="current"/> that are either absent from
    /// <paramref name="previous"/> or whose <see cref="InboxItem.UpdatedAt"/> has
    /// advanced since the previous snapshot. Order follows <paramref name="current"/>.
    /// </summary>
    public IReadOnlyList<InboxItem> FindNewOrUpdated(
        IReadOnlyCollection<InboxItem> previous,
        IReadOnlyCollection<InboxItem> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var previousByKey = new Dictionary<(ProviderKind, string), DateTimeOffset>();
        foreach (var item in previous)
        {
            // Keep the latest timestamp if the previous snapshot ever held duplicates.
            if (!previousByKey.TryGetValue(item.Key, out var seen) || item.UpdatedAt > seen)
            {
                previousByKey[item.Key] = item.UpdatedAt;
            }
        }

        var result = new List<InboxItem>();
        foreach (var item in current)
        {
            if (!previousByKey.TryGetValue(item.Key, out var previousUpdatedAt) || item.UpdatedAt > previousUpdatedAt)
            {
                result.Add(item);
            }
        }

        return result;
    }
}
