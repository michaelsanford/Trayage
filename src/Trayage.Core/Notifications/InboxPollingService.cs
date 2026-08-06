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

    // Raise a whole-cycle failure toast only after this many consecutive failed cycles, so a
    // single transient blip doesn't nag the user.
    private const int CycleFailuresBeforeAlert = 3;

    private IReadOnlyList<InboxItem> _previous = Array.Empty<InboxItem>();
    private bool _baselineEstablished;

    // Providers currently in a failing state, so we toast only on healthy→failing and
    // failing→healthy transitions rather than every cycle.
    private readonly HashSet<ProviderKind> _failingProviders = new();
    private int _consecutiveCycleFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
                _consecutiveCycleFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbox poll cycle failed.");
                _consecutiveCycleFailures++;
                if (_consecutiveCycleFailures == CycleFailuresBeforeAlert)
                {
                    notifier.ShowMessage(
                        "Trayage can't refresh",
                        "Trayage has failed to check for new activity several times in a row. Check your connection or reconnect your providers in Settings.");
                }
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
        var result = await inboxService.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var current = result.Items;

        // Surface provider health on every cycle (including the silent baseline cycle) so a
        // provider that's broken from the start still gets reported.
        SurfaceProviderHealth(result.FailedProviders);

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

    // Toasts once when a provider starts failing and once when it recovers, tracking state in
    // _failingProviders so a persistently-broken provider doesn't notify every cycle.
    private void SurfaceProviderHealth(IReadOnlyList<ProviderKind> failedThisCycle)
    {
        foreach (var provider in failedThisCycle)
        {
            if (_failingProviders.Add(provider))
            {
                var name = provider.DisplayName();
                notifier.ShowMessage(
                    $"{name} sync failed",
                    $"Trayage couldn't reach {name}, so you may be missing notifications. Try reconnecting it in Settings.");
            }
        }

        // Anything previously failing that didn't fail this cycle has recovered.
        var recovered = _failingProviders.Where(p => !failedThisCycle.Contains(p)).ToList();
        foreach (var provider in recovered)
        {
            _failingProviders.Remove(provider);
            var name = provider.DisplayName();
            notifier.ShowMessage($"{name} sync restored", $"Trayage is receiving {name} activity again.");
        }
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
