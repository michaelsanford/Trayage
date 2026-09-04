using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

public sealed class InboxAggregatorTests
{
    private readonly InboxAggregator _aggregator = new();

    [Fact]
    public void Merge_OrdersNewestFirst()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var older = TestData.Item("1", updatedAt: t0);
        var newer = TestData.Item("2", updatedAt: t0.AddHours(1));

        var merged = _aggregator.Merge(new[] { new[] { older }, new[] { newer } });

        Assert.Equal(new[] { "2", "1" }, merged.Select(i => i.Id));
    }

    [Fact]
    public void Merge_DeduplicatesByKey_KeepingMostRecent()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var stale = TestData.Item("1", updatedAt: t0);
        var fresh = TestData.Item("1", updatedAt: t0.AddHours(2));

        var merged = _aggregator.Merge(new[] { new[] { stale }, new[] { fresh } });

        var item = Assert.Single(merged);
        Assert.Equal(t0.AddHours(2), item.UpdatedAt);
    }

    [Fact]
    public void Merge_KeepsItemsFromDifferentProvidersSharingAnId()
    {
        var gh = TestData.Item("1");
        var bb = TestData.Item("1", ProviderKind.Bitbucket);

        var merged = _aggregator.Merge(new[] { new[] { gh }, new[] { bb } });

        Assert.Equal(2, merged.Count);
    }
    [Fact]
    public void Merge_KeepsItemsFromDifferentAccountsSharingAnId()
    {
        // Two GitHub accounts can legitimately see the same notification thread id. Before the
        // key carried the account, one of them was silently dropped.
        var work = TestData.Item("1", accountId: "work");
        var personal = TestData.Item("1", accountId: "personal");

        var merged = _aggregator.Merge(new[] { new[] { work }, new[] { personal } });

        Assert.Equal(2, merged.Count);
        Assert.Equal(new[] { "personal", "work" }, merged.Select(i => i.AccountId).Order());
    }

    [Fact]
    public void Merge_StillDeduplicatesWithinOneAccount()
    {
        var first = TestData.Item("1", accountId: "work");
        var second = TestData.Item("1", accountId: "work");

        Assert.Single(_aggregator.Merge(new[] { new[] { first }, new[] { second } }));
    }
}
