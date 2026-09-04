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
/// Performs a single inbox refresh cycle: queries every connected account, merges the
/// results, and publishes them to <see cref="InboxState"/>. An account that throws is
/// logged and skipped so one failing service can't blank the whole inbox. The polling
/// service drives this on a timer; the UI can also call it for a manual refresh.
/// </summary>
public sealed class InboxService(
    ProviderRegistry registry,
    InboxAggregator aggregator,
    InboxState state,
    ISettingsStore settings,
    ILogger<InboxService> logger)
{
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

        var merged = aggregator.Merge(perProvider);
        state.Set(merged);
        return new InboxRefreshResult(merged, failures);
    }
}
