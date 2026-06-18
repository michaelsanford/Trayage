using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Notifications;

namespace Trayage.Core.Tests;

public sealed class NotificationRuleEngineTests
{
    private readonly NotificationRuleEngine _engine = new();

    // Most tests don't exercise the recency override: a null window keeps the original
    // "read items never toast" behaviour, and the supplied `now` is then irrelevant.
    private IReadOnlyList<InboxItem> Select(
        IEnumerable<InboxItem> items,
        NotificationSettings settings,
        IReadOnlyCollection<string> watched,
        TimeSpan? recencyWindow = null,
        DateTimeOffset? now = null) =>
        _engine.SelectNotifiable(items, settings, watched, now ?? DateTimeOffset.UtcNow, recencyWindow);

    [Fact]
    public void ReviewRequest_NotifiesWhenEnabled()
    {
        var settings = new NotificationSettings { ReviewRequests = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.ReviewRequest) };

        Assert.Single(Select(items, settings, Array.Empty<string>()));
    }

    [Fact]
    public void ReviewRequest_SuppressedWhenDisabled()
    {
        var settings = new NotificationSettings { ReviewRequests = false };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.ReviewRequest) };

        Assert.Empty(Select(items, settings, Array.Empty<string>()));
    }

    [Fact]
    public void RepoActivity_OutsideWatchedRepo_IsSuppressed()
    {
        var settings = new NotificationSettings { WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.RepoActivity, repo: "some/other") };

        Assert.Empty(Select(items, settings, new[] { "octocat/hello-world" }));
    }

    [Fact]
    public void AnyActivity_OnWatchedRepo_NotifiesRegardlessOfKind()
    {
        var settings = new NotificationSettings
        {
            WatchedRepoActivity = true,
            ReviewRequests = false,
            MentionsAndAssignments = false,
            CiStatus = false,
        };
        var items = new[]
        {
            TestData.Item("1", kind: InboxItemKind.RepoActivity, repo: "acme/widgets"),
            TestData.Item("2", kind: InboxItemKind.CiStatus, repo: "acme/widgets"),
        };

        Assert.Equal(2, Select(items, settings, new[] { "acme/widgets" }).Count);
    }

    [Fact]
    public void WatchedRepoActivity_Disabled_StillHonoursPerKindToggles()
    {
        var settings = new NotificationSettings { WatchedRepoActivity = false, ReviewRequests = true };
        var items = new[]
        {
            TestData.Item("1", kind: InboxItemKind.ReviewRequest, repo: "acme/widgets"),
            TestData.Item("2", kind: InboxItemKind.RepoActivity, repo: "acme/widgets"),
        };

        var result = Select(items, settings, new[] { "acme/widgets" });

        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
    }

    [Fact]
    public void ReadItem_IsNeverNotified_WithoutRecencyWindow()
    {
        // Even a watched-repo item that would otherwise toast is skipped once it's read,
        // when the recency override is off (null window).
        var settings = new NotificationSettings { ReviewRequests = true, WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.ReviewRequest, repo: "acme/widgets", unread: false) };

        Assert.Empty(Select(items, settings, new[] { "acme/widgets" }));
    }

    [Fact]
    public void WatchedRepoMatch_IsCaseInsensitive()
    {
        var settings = new NotificationSettings { WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.RepoActivity, repo: "Acme/Widgets") };

        Assert.Single(Select(items, settings, new[] { "acme/widgets" }));
    }

    [Fact]
    public void Participating_NotifiesWhenEnabled_EvenInUnwatchedRepo()
    {
        var settings = new NotificationSettings { Participating = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.Participating, repo: "microsoft/winget-pkgs") };

        Assert.Single(Select(items, settings, Array.Empty<string>()));
    }

    [Fact]
    public void Participating_SuppressedWhenDisabled()
    {
        var settings = new NotificationSettings { Participating = false };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.Participating, repo: "microsoft/winget-pkgs") };

        Assert.Empty(Select(items, settings, Array.Empty<string>()));
    }

    [Fact]
    public void ReadItem_RecentlyUpdated_WithinWindow_IsNotified()
    {
        // A notifiable kind that GitHub already marks read still toasts when it was updated
        // within the recency window (bridges the web-vs-REST read-state desync).
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var now = updatedAt.AddMinutes(10);
        var settings = new NotificationSettings { MentionsAndAssignments = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.Mention, updatedAt: updatedAt, unread: false) };

        Assert.Single(Select(items, settings, Array.Empty<string>(), recencyWindow: TimeSpan.FromMinutes(30), now: now));
    }

    [Fact]
    public void ReadItem_UpdatedOutsideWindow_IsSuppressed()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var now = updatedAt.AddMinutes(10);
        var settings = new NotificationSettings { MentionsAndAssignments = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.Mention, updatedAt: updatedAt, unread: false) };

        Assert.Empty(Select(items, settings, Array.Empty<string>(), recencyWindow: TimeSpan.FromMinutes(5), now: now));
    }

    [Fact]
    public void ReadItem_Recent_ButNullWindow_IsSuppressed()
    {
        // With the switch off (null window) the recency override never applies.
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = new NotificationSettings { MentionsAndAssignments = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.Mention, updatedAt: updatedAt, unread: false) };

        Assert.Empty(Select(items, settings, Array.Empty<string>(), recencyWindow: null, now: updatedAt));
    }

    [Fact]
    public void ReadItem_Recent_StillHonoursKindFiltering()
    {
        // Recency relaxes the read gate only — a read+recent RepoActivity in an unwatched repo stays quiet.
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = new NotificationSettings { WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.RepoActivity, repo: "some/unwatched", updatedAt: updatedAt, unread: false) };

        Assert.Empty(Select(items, settings, Array.Empty<string>(), recencyWindow: TimeSpan.FromMinutes(30), now: updatedAt.AddMinutes(1)));
    }
}
