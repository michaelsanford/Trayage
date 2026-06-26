using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Merges per-provider results into a single ordered inbox. Pure and side-effect free
/// so it can be unit-tested in isolation.
/// </summary>
public sealed class InboxAggregator
{
    /// <summary>
    /// Flattens the supplied result sets, de-duplicates by <see cref="InboxItem.Key"/>
    /// (keeping the most recently updated copy), and orders newest-first.
    /// </summary>
    public IReadOnlyList<InboxItem> Merge(IEnumerable<IReadOnlyList<InboxItem>> providerResults)
    {
        ArgumentNullException.ThrowIfNull(providerResults);

        var byKey = new Dictionary<(ProviderKind, string), InboxItem>();

        foreach (var result in providerResults)
        {
            foreach (var item in result)
            {
                if (!byKey.TryGetValue(item.Key, out var existing) || item.UpdatedAt > existing.UpdatedAt)
                {
                    byKey[item.Key] = item;
                }
            }
        }

        return byKey.Values
            .OrderByDescending(i => i.UpdatedAt)
            .ThenBy(i => i.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
