using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;

namespace Trayage.Core.Tests;

public sealed class InboxServiceTests
{
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();
    private readonly InboxState _state = new();
    private readonly TrayageSettings _stored = new();

    public InboxServiceTests() =>
        _settings.Load().Returns(_stored);

    private InboxService NewService(params IInboxProvider[] providers) =>
        new(TestProviders.Registry(_settings, providers), new InboxAggregator(), _state, _settings,
            NullLogger<InboxService>.Instance);

    private static IInboxProvider Provider(
        ProviderKind kind,
        bool connected = true,
        params InboxItem[] items) =>
        TestProviders.Provider(kind, accountId: kind.ToString(), connected: connected, items: items);

    [Fact]
    public async Task RefreshAsync_OneProviderThrows_KeepsHealthyProviderItems()
    {
        var healthy = Provider(ProviderKind.GitHub, items: TestData.Item("gh1"));
        var failing = TestProviders.Stub(ProviderKind.Bitbucket, accountId: "bb");
        failing.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<InboxItem>>>(_ => throw new InvalidOperationException("boom"));

        var result = await NewService(healthy, failing).RefreshAsync(CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("gh1", result.Items[0].Id);
        Assert.Equal(new[] { "bb" }, result.Failures.Select(f => f.AccountId));
    }

    [Fact]
    public async Task RefreshAsync_SkipsDisconnectedProviders()
    {
        var disconnected = Provider(ProviderKind.GitHub, connected: false, items: TestData.Item("gh1"));

        var result = await NewService(disconnected).RefreshAsync(CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Empty(result.Failures);
        await disconnected.DidNotReceive().FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_PublishesMergedSnapshotAndRaisesChanged()
    {
        var provider = Provider(ProviderKind.GitHub, true, TestData.Item("gh1"), TestData.Item("gh2"));
        var changedRaised = 0;
        _state.Changed += (_, _) => changedRaised++;

        var result = await NewService(provider).RefreshAsync(CancellationToken.None);

        Assert.Equal(result.Items, _state.Items);
        Assert.Equal(1, changedRaised);
    }

    [Fact]
    public async Task RefreshAsync_CancellationRequested_RethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var token = cts.Token;

        var provider = TestProviders.Stub(ProviderKind.GitHub);
        provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<InboxItem>>>(_ => throw new OperationCanceledException(token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => NewService(provider).RefreshAsync(token));
    }

    [Fact]
    public async Task RefreshAsync_ForwardsTheAccountsOwnWatchedRepositories()
    {
        var provider = Provider(ProviderKind.GitHub);
        var service = NewService(provider);
        _stored.FindAccount("GitHub")!.WatchedRepositories.Add("acme/widgets");

        await service.RefreshAsync(CancellationToken.None);

        await provider.Received().FetchInboxAsync(
            Arg.Is<InboxQuery>(q => q.WatchedRepositories.Contains("acme/widgets")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_EachAccountGetsOnlyItsOwnWatchedRepositories()
    {
        // A repo one account can see must not be queried with another account's token.
        var first = TestProviders.Provider(ProviderKind.Bitbucket, accountId: "bb1");
        var second = TestProviders.Provider(ProviderKind.Bitbucket, accountId: "bb2");
        var service = NewService(first, second);
        _stored.FindAccount("bb1")!.WatchedRepositories.Add("first/only");
        _stored.FindAccount("bb2")!.WatchedRepositories.Add("second/only");

        await service.RefreshAsync(CancellationToken.None);

        await first.Received().FetchInboxAsync(
            Arg.Is<InboxQuery>(q => q.WatchedRepositories.SequenceEqual(new[] { "first/only" })),
            Arg.Any<CancellationToken>());
        await second.Received().FetchInboxAsync(
            Arg.Is<InboxQuery>(q => q.WatchedRepositories.SequenceEqual(new[] { "second/only" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_SkipsPausedAccounts()
    {
        var provider = Provider(ProviderKind.GitHub, items: TestData.Item("gh1"));
        var service = NewService(provider);
        _stored.FindAccount("GitHub")!.Enabled = false;

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Empty(result.Items);
        await provider.DidNotReceive().FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>());
    }
}
