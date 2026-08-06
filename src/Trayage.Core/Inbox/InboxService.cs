using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;
using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>
/// Outcome of one <see cref="InboxService.RefreshAsync"/> cycle: the merged snapshot plus the
/// connected providers that threw this cycle. <paramref name="FailedProviders"/> lets callers
/// surface a degraded provider (e.g. a toast) instead of silently serving a thinner inbox.
/// </summary>
public sealed record InboxRefreshResult(IReadOnlyList<InboxItem> Items, IReadOnlyList<ProviderKind> FailedProviders);

/// <summary>
/// Performs a single inbox refresh cycle: queries every connected provider, merges the
/// results, and publishes them to <see cref="InboxState"/>. A provider that throws is
/// logged and skipped so one failing service can't blank the whole inbox. The polling
/// service drives this on a timer; the UI can also call it for a manual refresh.
/// </summary>
public sealed class InboxService(
    IEnumerable<IInboxProvider> providers,
    InboxAggregator aggregator,
    InboxState state,
    ISettingsStore settings,
    ILogger<InboxService> logger)
{
    private readonly IReadOnlyList<IInboxProvider> _providers = providers.ToList();

    /// <summary>
    /// Fetches and publishes the current inbox, returning the merged snapshot along with any
    /// connected providers that failed this cycle. Never throws for provider-level failures.
    /// </summary>
    public async Task<InboxRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var query = new InboxQuery(settings.Load().WatchedRepositories);
        var perProvider = new List<IReadOnlyList<InboxItem>>(_providers.Count);
        var failed = new List<ProviderKind>();

        foreach (var provider in _providers)
        {
            if (!provider.IsConnected)
            {
                continue;
            }

            try
            {
                var items = await provider.FetchInboxAsync(query, cancellationToken).ConfigureAwait(false);
                perProvider.Add(items);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add(provider.Provider);
                logger.LogWarning(ex, "Provider {Provider} failed to fetch its inbox.", provider.Provider);
            }
        }

        var merged = aggregator.Merge(perProvider);
        state.Set(merged);
        return new InboxRefreshResult(merged, failed);
    }
}
