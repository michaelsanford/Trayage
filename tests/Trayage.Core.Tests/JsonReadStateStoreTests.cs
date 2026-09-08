using Microsoft.Extensions.Logging.Abstractions;
using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

/// <summary>
/// Covers the local read overlay. The contract that matters to a user is "marking read sticks,
/// but new activity still reaches me": a mark must survive polls and restarts, yet must never
/// swallow a genuinely newer version of the same thread.
/// </summary>
public sealed class JsonReadStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Marked = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trayage-readstate-{Guid.NewGuid():N}.json");

    private JsonReadStateStore NewStore() => new(NullLogger<JsonReadStateStore>.Instance, _path);

    [Fact]
    public void Apply_WithNoMarks_ReturnsTheSameItems()
    {
        var items = new[] { TestData.Item("a"), TestData.Item("b") };

        var applied = NewStore().Apply(items);

        Assert.Same(items, applied);
        Assert.All(applied, i => Assert.True(i.IsUnread));
    }

    [Fact]
    public void Apply_AfterMarkRead_ReportsTheItemReadAndExplicitlySo()
    {
        var item = TestData.Item("a", updatedAt: Marked);
        var store = NewStore();
        store.MarkRead(new[] { item });

        var applied = store.Apply(new[] { item });

        var only = Assert.Single(applied);
        Assert.False(only.IsUnread);
        // The flag the recency bridge keys on, so a marked item doesn't get pulled back on screen.
        Assert.True(only.IsExplicitlyRead);
    }

    [Fact]
    public void Apply_LeavesOtherItemsAlone()
    {
        var marked = TestData.Item("a", updatedAt: Marked);
        var other = TestData.Item("b", updatedAt: Marked);
        var store = NewStore();
        store.MarkRead(new[] { marked });

        var applied = store.Apply(new[] { marked, other });

        Assert.False(applied[0].IsUnread);
        Assert.True(applied[1].IsUnread);
    }

    /// <summary>
    /// The eviction rule, and the whole reason a mark records a timestamp rather than a bare
    /// flag: a comment landing after the mark has to come back as unread, or the toast for it
    /// would be silently swallowed.
    /// </summary>
    [Fact]
    public void Apply_WhenTheThreadMovedOnAfterTheMark_GoesBackToUnread()
    {
        var store = NewStore();
        store.MarkRead(new[] { TestData.Item("a", updatedAt: Marked) });

        var withNewActivity = TestData.Item("a", updatedAt: Marked.AddMinutes(1));
        var applied = store.Apply(new[] { withNewActivity });

        Assert.True(Assert.Single(applied).IsUnread);
    }

    [Fact]
    public void Apply_OnceTheMarkIsSpent_DoesNotComeBack()
    {
        var store = NewStore();
        store.MarkRead(new[] { TestData.Item("a", updatedAt: Marked) });
        var withNewActivity = TestData.Item("a", updatedAt: Marked.AddMinutes(1));

        store.Apply(new[] { withNewActivity });

        // Re-applying the *original* timestamp must not resurrect the dropped mark.
        var applied = store.Apply(new[] { TestData.Item("a", updatedAt: Marked) });
        Assert.True(Assert.Single(applied).IsUnread);
    }

    [Fact]
    public void MarkRead_SurvivesANewStoreOverTheSameFile()
    {
        var item = TestData.Item("a", updatedAt: Marked);
        NewStore().MarkRead(new[] { item });

        // A separate instance stands in for the next app launch.
        var applied = NewStore().Apply(new[] { item });

        Assert.False(Assert.Single(applied).IsUnread);
    }

    /// <summary>
    /// Two accounts on one service can surface the same underlying thread id, and the mark must
    /// not leak between them — the same reason <see cref="InboxItem.Key"/> includes the account.
    /// </summary>
    [Fact]
    public void MarkRead_IsScopedToTheAccount()
    {
        var mine = TestData.Item("a", accountId: "acct1", updatedAt: Marked);
        var theirs = TestData.Item("a", accountId: "acct2", updatedAt: Marked);
        var store = NewStore();
        store.MarkRead(new[] { mine });

        var applied = store.Apply(new[] { mine, theirs });

        Assert.False(applied[0].IsUnread);
        Assert.True(applied[1].IsUnread);
    }

    [Fact]
    public void Apply_DropsRecordsForItemsNobodyHasSeenInAges()
    {
        var stale = new Dictionary<string, ReadMark>
        {
            ["GitHub|acct1|a"] = new(Marked, DateTimeOffset.UtcNow.AddDays(-200)),
        };
        File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(stale));

        // Applying an unrelated snapshot is enough to prune; the pruned item isn't in it.
        NewStore().Apply(new[] { TestData.Item("b") });

        var remaining = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ReadMark>>(
            File.ReadAllText(_path));
        Assert.Empty(remaining!);
    }

    [Fact]
    public void Apply_KeepsRecordsForItemsStillOnScreen()
    {
        var item = TestData.Item("a", updatedAt: Marked);
        var store = NewStore();
        store.MarkRead(new[] { item });

        store.Apply(new[] { item });

        var remaining = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ReadMark>>(
            File.ReadAllText(_path));
        Assert.Single(remaining!);
    }

    [Fact]
    public void Load_WhenTheFileIsCorrupt_StartsEmptyRatherThanThrowing()
    {
        File.WriteAllText(_path, "{ not json");

        var items = new[] { TestData.Item("a") };
        var applied = NewStore().Apply(items);

        Assert.Same(items, applied);
    }

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }
}
