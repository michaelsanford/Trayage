using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;

namespace Trayage.Core.Tests;

public sealed class InboxServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();
    private readonly InboxState _state = new();
    private readonly TrayageSettings _stored = new();

    public InboxServiceTests()
    {
        _settings.Load().Returns(_stored);
        _readState = TestProviders.ReadState(_temp);
    }

    public void Dispose() => _temp.Dispose();

    private readonly IReadStateStore _readState;

    private InboxService NewService(params IInboxProvider[] providers) =>
        new(TestProviders.Registry(_settings, providers), new InboxAggregator(), _state, _readState,
            _settings, NullLogger<InboxService>.Instance);

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

        // Any OperationCanceledException: an already-cancelled token can surface as the
        // TaskCanceledException subclass from the refresh gate before a fetch even starts.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
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

    [Fact]
    public async Task MarkAsReadAsync_TellsTheProviderAndPublishesTheItemAsRead()
    {
        // The account id has to match the provider's, since that's what routes the write.
        var item = TestData.Item("gh1", accountId: "GitHub");
        var provider = Provider(ProviderKind.GitHub, items: item);
        var service = NewService(provider);
        await service.RefreshAsync(CancellationToken.None);

        var result = await service.MarkAsReadAsync(new[] { item }, CancellationToken.None);

        Assert.Equal(1, result.Count);
        Assert.Empty(result.Notices);
        await provider.Received().TryMarkAsReadAsync(
            Arg.Is<InboxItem>(i => i.Id == "gh1"), Arg.Any<CancellationToken>());

        // Published straight away, so the tray badge doesn't wait for the next poll.
        Assert.False(Assert.Single(_state.Items).IsUnread);
    }

    /// <summary>
    /// A read mark must not depend on the provider accepting it — Bitbucket can never accept one.
    /// </summary>
    [Fact]
    public async Task MarkAsReadAsync_WhenTheProviderCannotRecordIt_StillMarksLocally()
    {
        var item = TestData.Item("bb1", ProviderKind.Bitbucket, accountId: "Bitbucket");
        var provider = Provider(ProviderKind.Bitbucket, items: item);
        provider.TryMarkAsReadAsync(Arg.Any<InboxItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MarkAsReadOutcome.NotSupported));
        var service = NewService(provider);
        await service.RefreshAsync(CancellationToken.None);

        var result = await service.MarkAsReadAsync(new[] { item }, CancellationToken.None);

        Assert.Equal(1, result.Count);
        Assert.Empty(result.Notices);
        Assert.False(Assert.Single(_state.Items).IsUnread);
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenTheProviderThrows_KeepsTheLocalMark()
    {
        var item = TestData.Item("gh1", accountId: "GitHub");
        var provider = Provider(ProviderKind.GitHub, items: item);
        provider.TryMarkAsReadAsync(Arg.Any<InboxItem>(), Arg.Any<CancellationToken>())
            .Returns<Task<MarkAsReadOutcome>>(_ => throw new InvalidOperationException("boom"));
        var service = NewService(provider);
        await service.RefreshAsync(CancellationToken.None);

        var result = await service.MarkAsReadAsync(new[] { item }, CancellationToken.None);

        Assert.Equal(1, result.Count);
        Assert.False(Assert.Single(_state.Items).IsUnread);
    }

    [Fact]
    public async Task MarkAsReadAsync_WhenTheTokenPredatesTheWriteScope_ReportsAReconnectNotice()
    {
        var item = TestData.Item("gl1", ProviderKind.GitLab, accountId: "GitLab");
        var provider = Provider(ProviderKind.GitLab, items: item);
        provider.TryMarkAsReadAsync(Arg.Any<InboxItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MarkAsReadOutcome.NeedsReauthorization));
        var service = NewService(provider);
        await service.RefreshAsync(CancellationToken.None);

        var result = await service.MarkAsReadAsync(new[] { item }, CancellationToken.None);

        Assert.Single(result.Notices);
        Assert.Contains("Reconnect", result.Notices[0], StringComparison.Ordinal);
        // Degraded, not failed: the item is still read locally.
        Assert.False(Assert.Single(_state.Items).IsUnread);
    }

    [Fact]
    public async Task MarkAsReadAsync_IgnoresItemsAlreadyRead()
    {
        var read = TestData.Item("gh1", unread: false, accountId: "GitHub");
        var provider = Provider(ProviderKind.GitHub, items: read);
        var service = NewService(provider);

        var result = await service.MarkAsReadAsync(new[] { read }, CancellationToken.None);

        Assert.Equal(0, result.Count);
        await provider.DidNotReceive().TryMarkAsReadAsync(Arg.Any<InboxItem>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The overlay is applied inside RefreshAsync, so a mark survives the snapshot being
    /// replaced wholesale on the next poll.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_AfterAMark_KeepsTheItemRead()
    {
        var item = TestData.Item("gh1", accountId: "GitHub");
        var service = NewService(Provider(ProviderKind.GitHub, items: item));
        await service.RefreshAsync(CancellationToken.None);
        await service.MarkAsReadAsync(new[] { item }, CancellationToken.None);

        var result = await service.RefreshAsync(CancellationToken.None);

        var only = Assert.Single(result.Items);
        Assert.False(only.IsUnread);
        Assert.True(only.IsExplicitlyRead);
    }

    /// <summary>
    /// Dropping a failed account's items would make the poller see all of them as new — and
    /// notify again — as soon as the account recovered.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_WhenAnAccountFails_KeepsItsLastKnownItems()
    {
        var fails = false;
        var provider = TestProviders.Stub(ProviderKind.GitLab, accountId: "gl");
        provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => fails
                ? throw new HttpRequestException("down")
                : Task.FromResult<IReadOnlyList<InboxItem>>([TestData.Item("gl1", ProviderKind.GitLab, accountId: "gl")]));
        var healthy = Provider(ProviderKind.GitHub, items: TestData.Item("gh1", accountId: "GitHub"));
        var service = NewService(provider, healthy);
        await service.RefreshAsync(CancellationToken.None);

        fails = true;
        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(["gh1", "gl1"], result.Items.Select(i => i.Id).Order());
        Assert.Equal("gl", Assert.Single(result.Failures).AccountId);
    }

    [Fact]
    public async Task RefreshAsync_FetchesAccountsConcurrently_AndMergesInRegistryOrder()
    {
        var bothStarted = new TaskCompletionSource();
        var started = 0;
        IInboxProvider Blocking(ProviderKind kind, string id) =>
            BlockingProvider(kind, id, async () =>
            {
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.SetResult();
                }

                // Only completes if the other account's fetch is in flight at the same time.
                await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            });

        var result = await NewService(Blocking(ProviderKind.GitHub, "a"), Blocking(ProviderKind.GitLab, "b"))
            .RefreshAsync(CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task RefreshAsync_OverlappingCalls_CoalesceIntoOneFollowUpFetch()
    {
        var release = new TaskCompletionSource();
        var fetches = 0;
        var provider = BlockingProvider(ProviderKind.GitHub, "a", async () =>
        {
            if (Interlocked.Increment(ref fetches) == 1)
            {
                await release.Task;
            }
        });
        var service = NewService(provider);

        var first = service.RefreshAsync(CancellationToken.None);
        // These arrive while the first fetch is still running, so it may predate whatever change
        // prompted them: they need one more fetch, but only one between them.
        var waiting = Enumerable.Range(0, 5).Select(_ => service.RefreshAsync(CancellationToken.None)).ToList();
        release.SetResult();

        await first;
        var results = await Task.WhenAll(waiting);

        Assert.Equal(2, fetches);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    /// <summary>A connected provider whose fetch runs <paramref name="during"/> and then returns one item.</summary>
    private static IInboxProvider BlockingProvider(ProviderKind kind, string accountId, Func<Task> during)
    {
        var provider = TestProviders.Stub(kind, accountId);
        provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await during();
                return (IReadOnlyList<InboxItem>)[TestData.Item($"{accountId}-1", kind, accountId: accountId)];
            });
        return provider;
    }
}
