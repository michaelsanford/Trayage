using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.Core.Notifications;

/// <summary>
/// Periodically refreshes the inbox and raises toasts for genuinely new activity. The
/// first cycle after launch is silent (it only establishes a baseline) so the user
/// isn't flooded with notifications for items that were already waiting.
/// </summary>
public sealed class InboxPollingService(
    InboxService inboxService,
    InboxDiffer differ,
    NotificationRuleEngine ruleEngine,
    IToastNotifier notifier,
    ISettingsStore settings,
    IEnumerable<IInboxProvider> providers,
    ILogger<InboxPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);

    private IReadOnlyList<InboxItem> _previous = Array.Empty<InboxItem>();
    private bool _baselineEstablished;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbox poll cycle failed.");
            }

            try
            {
                await Task.Delay(NextInterval(), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // internal (not private) so the orchestration logic can be unit-tested directly,
    // sidestepping the ≥30s delay in the ExecuteAsync loop. See InternalsVisibleTo in the .csproj.
    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var current = await inboxService.RefreshAsync(cancellationToken).ConfigureAwait(false);

        if (!_baselineEstablished)
        {
            _baselineEstablished = true;
            _previous = current;
            return;
        }

        var newItems = differ.FindNewOrUpdated(_previous, current);
        if (newItems.Count > 0)
        {
            var appSettings = settings.Load();
            var toNotify = ruleEngine.SelectNotifiable(
                newItems,
                appSettings.Notifications,
                appSettings.WatchedRepositories,
                DateTimeOffset.UtcNow,
                InboxRecency.WindowFor(appSettings));
            foreach (var item in toNotify)
            {
                notifier.Show(item);
            }

            if (toNotify.Count > 0)
            {
                logger.LogInformation("Raised {Count} notification(s) for new activity.", toNotify.Count);
            }
        }

        _previous = current;
    }

    internal TimeSpan NextInterval()
    {
        var configured = TimeSpan.FromSeconds(Math.Max(settings.Load().PollIntervalSeconds, 1));

        // Never poll faster than any provider recommends, nor faster than our own floor.
        var providerFloor = providers
            .Select(p => p.SuggestedPollInterval)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        var interval = configured;
        if (providerFloor > interval)
        {
            interval = providerFloor;
        }

        return interval < MinimumInterval ? MinimumInterval : interval;
    }
}
