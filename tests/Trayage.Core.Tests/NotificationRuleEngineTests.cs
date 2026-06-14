using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Notifications;

namespace Trayage.Core.Tests;

public sealed class NotificationRuleEngineTests
{
    private readonly NotificationRuleEngine _engine = new();

    [Fact]
    public void ReviewRequest_NotifiesWhenEnabled()
    {
        var settings = new NotificationSettings { ReviewRequests = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.ReviewRequest) };

        Assert.Single(_engine.SelectNotifiable(items, settings, Array.Empty<string>()));
    }

    [Fact]
    public void ReviewRequest_SuppressedWhenDisabled()
    {
        var settings = new NotificationSettings { ReviewRequests = false };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.ReviewRequest) };

        Assert.Empty(_engine.SelectNotifiable(items, settings, Array.Empty<string>()));
    }

    [Fact]
    public void RepoActivity_OutsideWatchedRepo_IsSuppressed()
    {
        var settings = new NotificationSettings { WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.RepoActivity, repo: "some/other") };

        Assert.Empty(_engine.SelectNotifiable(items, settings, new[] { "octocat/hello-world" }));
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

        Assert.Equal(2, _engine.SelectNotifiable(items, settings, new[] { "acme/widgets" }).Count);
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

        var result = _engine.SelectNotifiable(items, settings, new[] { "acme/widgets" });

        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
    }

    [Fact]
    public void ReadItem_IsNeverNotified()
    {
        // Even a watched-repo item that would otherwise toast is skipped once it's read.
        var settings = new NotificationSettings { ReviewRequests = true, WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.ReviewRequest, repo: "acme/widgets", unread: false) };

        Assert.Empty(_engine.SelectNotifiable(items, settings, new[] { "acme/widgets" }));
    }

    [Fact]
    public void WatchedRepoMatch_IsCaseInsensitive()
    {
        var settings = new NotificationSettings { WatchedRepoActivity = true };
        var items = new[] { TestData.Item("1", kind: InboxItemKind.RepoActivity, repo: "Acme/Widgets") };

        Assert.Single(_engine.SelectNotifiable(items, settings, new[] { "acme/widgets" }));
    }
}
