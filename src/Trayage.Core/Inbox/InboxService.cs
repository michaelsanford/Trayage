using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Providers;

namespace Trayage.Core.Inbox;

/// <summary>
/// One account that failed to fetch this cycle. Carries the account id (not just the provider)
/// so two accounts on the same service are reported — and recover — independently.
/// </summary>
public sealed record ProviderFailure(string AccountId, ProviderKind Provider, string Label);

/// <summary>
/// Outcome of one <see cref="InboxService.RefreshAsync"/> cycle: the merged snapshot plus the
/// connected accounts that threw this cycle. <paramref name="Failures"/> lets callers
/// surface a degraded account (e.g. a toast) instead of silently serving a thinner inbox.
/// </summary>
public sealed record InboxRefreshResult(IReadOnlyList<InboxItem> Items, IReadOnlyList<ProviderFailure> Failures);

/// <summary>
/// Outcome of marking items read. <paramref name="Notices"/> carries anything the user needs to
/// act on — today, accounts whose token predates the write scope and must be reconnected. The
/// local mark always succeeds, so this is advisory rather than an error report.
/// </summary>
public sealed record MarkAsReadResult(int Count, IReadOnlyList<string> Notices);

/// <summary>
/// Performs a single inbox refresh cycle: queries every connected account, merges the
/// results, and publishes them to <see cref="InboxState"/>. An account that throws is
/// logged and skipped so one failing service can't blank the whole inbox. The polling
/// service drives this on a timer; the UI can also call it for a manual refresh.
/// </summary>
public sealed class InboxService(
    ProviderRegistry registry,
    InboxAggregator aggregator,
    InboxState state,
    IReadStateStore readState,
    ISettingsStore settings,
    ILogger<InboxService> logger)
{
    /// <summary>
    /// Caps in-flight provider writes when marking a group or the whole inbox read, so a
    /// hundred-item "mark all read" isn't a hundred serial round-trips — nor a hundred
    /// simultaneous ones against an API that rate-limits.
    /// </summary>
    private const int MaxConcurrentMarkWrites = 4;

    /// <summary>
    /// Fetches and publishes the current inbox, returning the merged snapshot along with any
    /// accounts that failed this cycle. Never throws for provider-level failures.
    /// </summary>
    public async Task<InboxRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        // Read the registry per cycle, not once at construction, so an account connected a
        // moment ago is polled immediately rather than after a restart.
        var providers = registry.Active;
        var accounts = settings.Load().Accounts.ToDictionary(a => a.Id, StringComparer.Ordinal);

        var perProvider = new List<IReadOnlyList<InboxItem>>(providers.Count);
        var failures = new List<ProviderFailure>();

        foreach (var provider in providers)
        {
            // Watched repositories are scoped to the account: querying a repo with a token that
            // can't see it would just 404 every cycle.
            var watched = accounts.TryGetValue(provider.AccountId, out var account)
                ? account.WatchedRepositories
                : new List<string>();

            try
            {
                var items = await provider.FetchInboxAsync(new InboxQuery(watched), cancellationToken).ConfigureAwait(false);
                perProvider.Add(items);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new ProviderFailure(provider.AccountId, provider.Provider, provider.DisplayLabel));
                logger.LogWarning(ex, "Account {AccountId} on {Provider} failed to fetch its inbox.",
                    provider.AccountId, provider.Provider);
            }
        }

        // The overlay goes on here, before anything downstream sees the snapshot: the polling
        // service feeds these same items to the differ and the toast gate, so a locally-marked
        // item stays quiet — and one whose thread has new activity loses its mark here and
        // correctly notifies again.
        var merged = readState.Apply(aggregator.Merge(perProvider));
        state.Set(merged);
        return new InboxRefreshResult(merged, failures);
    }

    /// <summary>
    /// Marks items read: locally first, so the change is immediate and durable even offline,
    /// then on each provider that can record it. Republishes the snapshot so the tray badge and
    /// flyout update without waiting for the next poll. Never throws for provider failures.
    /// </summary>
    public async Task<MarkAsReadResult> MarkAsReadAsync(
        IReadOnlyCollection<InboxItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var unread = items.Where(i => i.IsUnread).ToList();
        if (unread.Count == 0)
        {
            return new MarkAsReadResult(0, Array.Empty<string>());
        }

        readState.MarkRead(unread);

        // Republish immediately from the current snapshot rather than refetching: Apply is what
        // turns the new marks into read items, and a refetch would make the UI wait on the network.
        state.Set(readState.Apply(state.Items));

        var providers = registry.Active.ToDictionary(p => p.AccountId, StringComparer.Ordinal);
        var notices = new HashSet<string>(StringComparer.Ordinal);
        using var gate = new SemaphoreSlim(MaxConcurrentMarkWrites);

        var writes = unread.Select(async item =>
        {
            if (!providers.TryGetValue(item.AccountId, out var provider))
            {
                return;
            }

            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var outcome = await provider.TryMarkAsReadAsync(item, cancellationToken).ConfigureAwait(false);
                if (outcome == MarkAsReadOutcome.NeedsReauthorization)
                {
                    lock (notices)
                    {
                        notices.Add($"Reconnect {provider.DisplayLabel} to mark items read there.");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The local mark stands, so a failed write is a degraded outcome, not a lost action.
                logger.LogWarning(ex, "Account {AccountId} on {Provider} failed to mark an item read.",
                    provider.AccountId, provider.Provider);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(writes).ConfigureAwait(false);

        logger.LogInformation("Marked {Count} item(s) read.", unread.Count);
        return new MarkAsReadResult(unread.Count, notices.ToList());
    }
}
