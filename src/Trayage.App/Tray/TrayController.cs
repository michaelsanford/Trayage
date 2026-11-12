using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trayage.App.ViewModels;
using Trayage.App.Views;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Providers;
#if DEBUG
using Trayage.Core.Models;
using Trayage.Core.Notifications;
#endif

namespace Trayage.App.Tray;

/// <summary>
/// Connects the tray icon to the rest of the app once the host is up: menu actions open the
/// inbox flyout, Settings or a refresh, and the icon follows connection and unread state.
/// <see cref="App"/> keeps the process lifecycle; this owns what the tray shows and does.
/// </summary>
internal sealed class TrayController(IServiceProvider services, TrayIconService tray, ILogger logger)
{
    // Set by the first InboxState.Changed (raised on the poller's thread).
    private volatile bool _firstSnapshotReceived;

    private InboxFlyout Flyout => services.GetRequiredService<InboxFlyout>();

    /// <summary>
    /// Wires the menu and state subscriptions and enables the tray. Call on the UI thread,
    /// before the host starts, so the first poll's snapshot can't be missed.
    /// </summary>
    public void Attach()
    {
        tray.LeftClicked += () => Flyout.Toggle();
        tray.OpenInboxRequested += ShowInbox;
        tray.RefreshRequested += RefreshInbox;
        tray.SettingsRequested += ShowSettings;
#if DEBUG
        tray.InjectRequested += InjectItem;
#endif

        var inboxViewModel = services.GetRequiredService<InboxViewModel>();
        inboxViewModel.OpenSettingsRequested += ShowSettings;

        // Apply inbox display options (grouping / show-read) immediately when toggled.
        services.GetRequiredService<SettingsViewModel>().InboxDisplayChanged += inboxViewModel.ApplyDisplaySettings;

        // Connect and disconnect both trigger a refresh, so the inbox subscription also
        // catches sign-in/out; the registry one catches accounts being added or removed.
        services.GetRequiredService<InboxState>().Changed += OnInboxStateChanged;
        services.GetRequiredService<ProviderRegistry>().Changed += (_, _) => RefreshStatus();

        tray.SetReady(true);
        RefreshStatus();
    }

    public void ShowInbox() => Flyout.ShowNearTray();

    private void ShowSettings() => services.GetRequiredService<SettingsWindow>().ShowAndActivate();

    private void RefreshInbox() => _ = services.GetRequiredService<InboxService>().RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Every published snapshot re-renders the tray. The first one also ends the faded
    /// "syncing" state shown since launch.
    /// </summary>
    private void OnInboxStateChanged(object? sender, EventArgs e)
    {
        if (!_firstSnapshotReceived)
        {
            _firstSnapshotReceived = true;
            logger.LogInformation("First inbox snapshot ({Elapsed} ms after process start).", StartupClock.ElapsedMilliseconds());
        }

        RefreshStatus();
    }

    /// <summary>
    /// Recomputes the tray icon state. Grey ("not connected") means no account has been set up
    /// at all; once at least one is configured the icon follows its connection — unread or
    /// caught up — and shows faded bars until the first poll has finished, rather than flashing
    /// "connection problem" while sessions are still being restored. May be called off the UI
    /// thread (e.g. from <see cref="InboxState.Changed"/>); the icon swap itself is marshalled.
    /// </summary>
    private void RefreshStatus()
    {
        var settings = services.GetRequiredService<ISettingsStore>().Load();
        var configured = settings.Accounts.Any(a => a.Connected);
        var liveConnected = services.GetRequiredService<ProviderRegistry>().All.Any(p => p.IsConnected);
        var inboxState = services.GetRequiredService<InboxState>();

        // Reason matters for the disconnected glyph: a provider that is configured but has no
        // live session shows red (connection problem), while nothing-configured shows grey.
        var (status, connectionError) = (configured, liveConnected) switch
        {
            (true, _) when !_firstSnapshotReceived => (TrayStatus.Syncing, false),
            (_, true) => (inboxState.HasUnread ? TrayStatus.Unread : TrayStatus.CaughtUp, false),
            _ => (TrayStatus.Disconnected, configured),
        };

        tray.SetStatus(status, inboxState.UnreadCount, connectionError);
    }

#if DEBUG
    // Debug-only developer aid, compiled out of Release/shipping builds. Wired to the tray
    // "Inject ▸ <provider> ▸ <kind>" menu; see TrayIconService for the entries.
    //
    // Injects a synthetic item for the chosen provider/kind into the live stream — it lands
    // in InboxState (so the flyout + tray update immediately) and runs through the real
    // NotificationRuleEngine + notifier, exactly as the poller does, so you can see how the
    // pipeline responds to your current settings. The next poll overwrites the snapshot with
    // real provider data, so the injected item is transient.
    private void InjectItem(ProviderKind provider, InboxItemKind kind)
    {
        var item = SampleItem(provider, kind);

        var state = services.GetRequiredService<InboxState>();
        var settings = services.GetRequiredService<ISettingsStore>().Load();
        var rules = services.GetRequiredService<NotificationRuleEngine>();
        var notifier = services.GetRequiredService<IToastNotifier>();

        state.Set(new List<InboxItem>(state.Items) { item });

        foreach (var notifiable in rules.SelectNotifiable([item], settings.Notifications, settings.AllWatchedRepositories, DateTimeOffset.UtcNow, InboxRecency.WindowFor(settings)))
        {
            notifier.Show(notifiable);
        }
    }

    private static InboxItem SampleItem(ProviderKind provider, InboxItemKind kind)
    {
        var webUrl = provider switch
        {
            ProviderKind.Bitbucket => "https://bitbucket.org/michaelsanford/trayage/pull-requests/1",
            ProviderKind.GitLab => "https://gitlab.com/michaelsanford/trayage/-/merge_requests/1",
            _ => "https://github.com/michaelsanford/Trayage/pull/1",
        };

        return new()
        {
            Id = $"debug-{provider}-{kind}-{DateTimeOffset.UtcNow.Ticks}",
            Provider = provider,
            AccountId = "debug",
            Kind = kind,
            Title = $"[debug] {provider} {kind} — click me",
            RepositoryFullName = "michaelsanford/Trayage",
            WebUrl = webUrl,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
#endif
}
