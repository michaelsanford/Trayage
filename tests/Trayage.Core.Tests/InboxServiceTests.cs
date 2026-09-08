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

    private readonly IReadStateStore _readState = TestProviders.ReadState();

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
}
