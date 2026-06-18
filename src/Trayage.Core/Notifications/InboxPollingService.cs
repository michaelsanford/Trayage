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
public sealed class InboxPollingService : BackgroundService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);

    private readonly InboxService _inboxService;
    private readonly InboxDiffer _differ;
    private readonly NotificationRuleEngine _ruleEngine;
    private readonly IToastNotifier _notifier;
    private readonly ISettingsStore _settings;
    private readonly IEnumerable<IInboxProvider> _providers;
    private readonly ILogger<InboxPollingService> _logger;

    private IReadOnlyList<InboxItem> _previous = Array.Empty<InboxItem>();
    private bool _baselineEstablished;

    public InboxPollingService(
        InboxService inboxService,
        InboxDiffer differ,
        NotificationRuleEngine ruleEngine,
        IToastNotifier notifier,
        ISettingsStore settings,
        IEnumerable<IInboxProvider> providers,
        ILogger<InboxPollingService> logger)
    {
        _inboxService = inboxService;
        _differ = differ;
        _ruleEngine = ruleEngine;
        _notifier = notifier;
        _settings = settings;
        _providers = providers;
        _logger = logger;
    }

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
                _logger.LogError(ex, "Inbox poll cycle failed.");
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

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var current = await _inboxService.RefreshAsync(cancellationToken).ConfigureAwait(false);

        if (!_baselineEstablished)
        {
            _baselineEstablished = true;
            _previous = current;
            return;
        }

        var newItems = _differ.FindNewOrUpdated(_previous, current);
        if (newItems.Count > 0)
        {
            var settings = _settings.Load();
            var toNotify = _ruleEngine.SelectNotifiable(
                newItems,
                settings.Notifications,
                settings.WatchedRepositories,
                DateTimeOffset.UtcNow,
                InboxRecency.WindowFor(settings));
            foreach (var item in toNotify)
            {
                _notifier.Show(item);
            }

            if (toNotify.Count > 0)
            {
                _logger.LogInformation("Raised {Count} notification(s) for new activity.", toNotify.Count);
            }
        }

        _previous = current;
    }

    private TimeSpan NextInterval()
    {
        var configured = TimeSpan.FromSeconds(Math.Max(_settings.Load().PollIntervalSeconds, 1));

        // Never poll faster than any provider recommends, nor faster than our own floor.
        var providerFloor = _providers
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
