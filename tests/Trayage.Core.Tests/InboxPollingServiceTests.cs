using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Notifications;

namespace Trayage.Core.Tests;

public sealed class InboxPollingServiceTests
{
    private readonly IToastNotifier _notifier = Substitute.For<IToastNotifier>();
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();

    /// <summary>
    /// Builds a polling service whose single provider returns each supplied snapshot on
    /// successive <see cref="IInboxProvider.FetchInboxAsync"/> calls (one per poll cycle).
    /// </summary>
    private InboxPollingService NewService(
        TrayageSettings settings,
        TimeSpan? suggestedPollInterval = null,
        params IReadOnlyList<InboxItem>[] snapshotsPerPoll)
    {
        _settings.Load().Returns(settings);

        var provider = Substitute.For<IInboxProvider>();
        provider.Provider.Returns(ProviderKind.GitHub);
        provider.IsConnected.Returns(true);
        provider.SuggestedPollInterval.Returns(suggestedPollInterval);
        if (snapshotsPerPoll.Length > 0)
        {
            var tasks = snapshotsPerPoll.Select(Task.FromResult).ToArray();
            provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
                .Returns(tasks[0], tasks.Skip(1).ToArray());
        }

        var providers = new[] { provider };
        var inboxService = new InboxService(
            providers, new InboxAggregator(), new InboxState(), _settings, NullLogger<InboxService>.Instance);

        return new InboxPollingService(
            inboxService,
            new InboxDiffer(),
            new NotificationRuleEngine(),
            _notifier,
            _settings,
            providers,
            NullLogger<InboxPollingService>.Instance);
    }

    private static IReadOnlyList<InboxItem> Snapshot(params InboxItem[] items) => items;

    [Fact]
    public async Task FirstPoll_IsSilent_EvenWithItems()
    {
        var service = NewService(
            new TrayageSettings(),
            snapshotsPerPoll: Snapshot(TestData.Item("1")));

        await service.PollOnceAsync(CancellationToken.None);

        _notifier.DidNotReceive().Show(Arg.Any<InboxItem>());
    }

    [Fact]
    public async Task SecondPoll_NotifiesOnlyGenuinelyNewItems()
    {
        var service = NewService(
            new TrayageSettings(),
            snapshotsPerPoll: new[]
            {
                Snapshot(TestData.Item("1")),
                Snapshot(TestData.Item("1"), TestData.Item("2")),
            });

        await service.PollOnceAsync(CancellationToken.None); // baseline
        await service.PollOnceAsync(CancellationToken.None);

        _notifier.Received(1).Show(Arg.Is<InboxItem>(i => i.Id == "2"));
        _notifier.DidNotReceive().Show(Arg.Is<InboxItem>(i => i.Id == "1"));
    }

    [Fact]
    public async Task SecondPoll_UnchangedSnapshot_NotifiesNothing()
    {
        var service = NewService(
            new TrayageSettings(),
            snapshotsPerPoll: new[]
            {
                Snapshot(TestData.Item("1")),
                Snapshot(TestData.Item("1")),
            });

        await service.PollOnceAsync(CancellationToken.None);
        await service.PollOnceAsync(CancellationToken.None);

        _notifier.DidNotReceive().Show(Arg.Any<InboxItem>());
    }

    [Fact]
    public async Task RecencyWindow_On_ReadButRecentItem_IsNotified()
    {
        // A read item updated "in the future" relative to the poll's UtcNow is always within
        // any window, so this stays deterministic regardless of the machine clock.
        var recentRead = TestData.Item("1", updatedAt: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), unread: false);
        var service = NewService(
            new TrayageSettings { SurfaceRecentlyModified = true },
            snapshotsPerPoll: new[] { Snapshot(), Snapshot(recentRead) });

        await service.PollOnceAsync(CancellationToken.None); // baseline (empty)
        await service.PollOnceAsync(CancellationToken.None);

        _notifier.Received(1).Show(Arg.Is<InboxItem>(i => i.Id == "1"));
    }

    [Fact]
    public async Task RecencyWindow_Off_ReadItem_IsSuppressed()
    {
        var recentRead = TestData.Item("1", updatedAt: new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero), unread: false);
        var service = NewService(
            new TrayageSettings { SurfaceRecentlyModified = false },
            snapshotsPerPoll: new[] { Snapshot(), Snapshot(recentRead) });

        await service.PollOnceAsync(CancellationToken.None);
        await service.PollOnceAsync(CancellationToken.None);

        _notifier.DidNotReceive().Show(Arg.Any<InboxItem>());
    }

    [Fact]
    public void NextInterval_TinyConfigured_ClampsToThirtySecondFloor()
    {
        var service = NewService(new TrayageSettings { PollIntervalSeconds = 1 });

        Assert.Equal(TimeSpan.FromSeconds(30), service.NextInterval());
    }

    [Fact]
    public void NextInterval_ProviderFloorExceedsConfigured_UsesProviderFloor()
    {
        var service = NewService(
            new TrayageSettings { PollIntervalSeconds = 60 },
            suggestedPollInterval: TimeSpan.FromSeconds(120));

        Assert.Equal(TimeSpan.FromSeconds(120), service.NextInterval());
    }

    [Fact]
    public void NextInterval_ConfiguredExceedsProviderFloor_UsesConfigured()
    {
        var service = NewService(
            new TrayageSettings { PollIntervalSeconds = 600 },
            suggestedPollInterval: TimeSpan.FromSeconds(120));

        Assert.Equal(TimeSpan.FromSeconds(600), service.NextInterval());
    }
}
