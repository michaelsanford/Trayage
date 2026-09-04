using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;

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
    ProviderRegistry registry,
    ILogger<InboxPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);

    // Raise a whole-cycle failure toast only after this many consecutive failed cycles, so a
    // single transient blip doesn't nag the user.
    private const int CycleFailuresBeforeAlert = 3;

    private IReadOnlyList<InboxItem> _previous = Array.Empty<InboxItem>();
    private bool _baselineEstablished;

    // Accounts currently in a failing state, so we toast only on healthy→failing and
    // failing→healthy transitions rather than every cycle. Keyed by account, not provider, so
    // one GitHub account recovering doesn't clear another's alert. The label is kept alongside
    // so a recovery can still be named after the account row is gone.
    private readonly Dictionary<string, string> _failingAccounts = new(StringComparer.Ordinal);
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
        SurfaceProviderHealth(result.Failures);

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
                appSettings.AllWatchedRepositories,
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

    // Toasts once when an account starts failing and once when it recovers, tracking state in
    // _failingAccounts so a persistently-broken account doesn't notify every cycle.
    private void SurfaceProviderHealth(IReadOnlyList<ProviderFailure> failedThisCycle)
    {
        foreach (var failure in failedThisCycle)
        {
            if (_failingAccounts.TryAdd(failure.AccountId, failure.Label))
            {
                notifier.ShowMessage(
                    $"{failure.Label} sync failed",
                    $"Trayage couldn't reach {failure.Label}, so you may be missing notifications. Try reconnecting it in Settings.");
            }
        }

        // Anything previously failing that didn't fail this cycle has recovered.
        var stillFailing = failedThisCycle.Select(f => f.AccountId).ToHashSet(StringComparer.Ordinal);
        var recovered = _failingAccounts.Where(kv => !stillFailing.Contains(kv.Key)).ToList();
        foreach (var (accountId, label) in recovered)
        {
            _failingAccounts.Remove(accountId);
            notifier.ShowMessage($"{label} sync restored", $"Trayage is receiving {label} activity again.");
        }
    }

    internal TimeSpan NextInterval()
    {
        var configured = TimeSpan.FromSeconds(Math.Max(settings.Load().PollIntervalSeconds, 1));

        // Never poll faster than any provider recommends, nor faster than our own floor.
        var providerFloor = registry.All
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
