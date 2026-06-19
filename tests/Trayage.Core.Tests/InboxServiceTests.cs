using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

public sealed class InboxServiceTests
{
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();
    private readonly InboxState _state = new();

    public InboxServiceTests() =>
        _settings.Load().Returns(new TrayageSettings());

    private InboxService NewService(params IInboxProvider[] providers) =>
        new(providers, new InboxAggregator(), _state, _settings, NullLogger<InboxService>.Instance);

    private static IInboxProvider Provider(
        ProviderKind kind,
        bool connected = true,
        params InboxItem[] items)
    {
        var provider = Substitute.For<IInboxProvider>();
        provider.Provider.Returns(kind);
        provider.IsConnected.Returns(connected);
        provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<InboxItem>>(items));
        return provider;
    }

    [Fact]
    public async Task RefreshAsync_OneProviderThrows_KeepsHealthyProviderItems()
    {
        var healthy = Provider(ProviderKind.GitHub, items: TestData.Item("gh1"));
        var failing = Substitute.For<IInboxProvider>();
        failing.Provider.Returns(ProviderKind.Bitbucket);
        failing.IsConnected.Returns(true);
        failing.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<InboxItem>>>(_ => throw new InvalidOperationException("boom"));

        var result = await NewService(healthy, failing).RefreshAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("gh1", result[0].Id);
    }

    [Fact]
    public async Task RefreshAsync_SkipsDisconnectedProviders()
    {
        var disconnected = Provider(ProviderKind.GitHub, connected: false, items: TestData.Item("gh1"));

        var result = await NewService(disconnected).RefreshAsync(CancellationToken.None);

        Assert.Empty(result);
        await disconnected.DidNotReceive().FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_PublishesMergedSnapshotAndRaisesChanged()
    {
        var provider = Provider(ProviderKind.GitHub, true, TestData.Item("gh1"), TestData.Item("gh2"));
        var changedRaised = 0;
        _state.Changed += (_, _) => changedRaised++;

        var result = await NewService(provider).RefreshAsync(CancellationToken.None);

        Assert.Equal(result, _state.Items);
        Assert.Equal(1, changedRaised);
    }

    [Fact]
    public async Task RefreshAsync_CancellationRequested_RethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var provider = Substitute.For<IInboxProvider>();
        provider.Provider.Returns(ProviderKind.GitHub);
        provider.IsConnected.Returns(true);
        provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<InboxItem>>>(_ => throw new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => NewService(provider).RefreshAsync(cts.Token));
    }

    [Fact]
    public async Task RefreshAsync_ForwardsWatchedRepositoriesToProviders()
    {
        _settings.Load().Returns(new TrayageSettings { WatchedRepositories = { "acme/widgets" } });
        var provider = Provider(ProviderKind.GitHub);

        await NewService(provider).RefreshAsync(CancellationToken.None);

        await provider.Received().FetchInboxAsync(
            Arg.Is<InboxQuery>(q => q.WatchedRepositories.Contains("acme/widgets")),
            Arg.Any<CancellationToken>());
    }
}
