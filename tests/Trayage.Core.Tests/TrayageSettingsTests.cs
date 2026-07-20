using Trayage.Core.Configuration;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

public sealed class TrayageSettingsTests
{
    [Fact]
    public void Clone_CopiesScalarValues()
    {
        var original = new TrayageSettings
        {
            PollIntervalSeconds = 120,
            Theme = AppTheme.Dark,
            StartWithWindows = true,
            SurfaceRecentlyModified = false,
        };
        original.Notifications.Style = NotificationStyle.AudioOnly;
        original.Notifications.Sound = "Magic";

        var clone = original.Clone();

        Assert.Equal(120, clone.PollIntervalSeconds);
        Assert.Equal(AppTheme.Dark, clone.Theme);
        Assert.True(clone.StartWithWindows);
        Assert.False(clone.SurfaceRecentlyModified);
        Assert.Equal(NotificationStyle.AudioOnly, clone.Notifications.Style);
        Assert.Equal("Magic", clone.Notifications.Sound);
    }

    [Fact]
    public void Clone_DeepCopies_MutatingCloneLeavesOriginalUntouched()
    {
        var original = new TrayageSettings { WatchedRepositories = { "a/b" } };
        original.Notifications.ReviewRequests = true;
        original.Notifications.Style = NotificationStyle.Both;
        original.Notifications.Sound = "Glass";
        original.GitHub.Connected = true;
        original.GitHub.AccountLogin = "octocat";
        original.Bitbucket.AccountLogin = "stelvio";
        original.GitLab.Connected = true;
        original.GitLab.AccountLogin = "tanuki";

        var clone = original.Clone();
        clone.WatchedRepositories.Add("x/y");
        clone.Notifications.ReviewRequests = false;
        clone.Notifications.Style = NotificationStyle.AudioOnly;
        clone.Notifications.Sound = "Eol";
        clone.GitHub.Connected = false;
        clone.GitHub.AccountLogin = "changed";
        clone.Bitbucket.AccountLogin = "changed";
        clone.GitLab.Connected = false;
        clone.GitLab.AccountLogin = "changed";

        Assert.Equal(new[] { "a/b" }, original.WatchedRepositories);
        Assert.True(original.Notifications.ReviewRequests);
        Assert.Equal(NotificationStyle.Both, original.Notifications.Style);
        Assert.Equal("Glass", original.Notifications.Sound);
        Assert.True(original.GitHub.Connected);
        Assert.Equal("octocat", original.GitHub.AccountLogin);
        Assert.Equal("stelvio", original.Bitbucket.AccountLogin);
        Assert.True(original.GitLab.Connected);
        Assert.Equal("tanuki", original.GitLab.AccountLogin);
    }

    [Fact]
    public void Clone_DeepCopies_NestedObjectsAreDistinctInstances()
    {
        var original = new TrayageSettings();
        var clone = original.Clone();

        Assert.NotSame(original.Notifications, clone.Notifications);
        Assert.NotSame(original.WatchedRepositories, clone.WatchedRepositories);
        Assert.NotSame(original.GitHub, clone.GitHub);
        Assert.NotSame(original.Bitbucket, clone.Bitbucket);
        Assert.NotSame(original.GitLab, clone.GitLab);
    }

    [Theory]
    [InlineData(InboxItemKind.ReviewRequest, true)]
    [InlineData(InboxItemKind.Mention, true)]
    [InlineData(InboxItemKind.Assignment, true)]
    [InlineData(InboxItemKind.CiStatus, false)]
    [InlineData(InboxItemKind.RepoActivity, true)]
    [InlineData(InboxItemKind.Participating, true)]
    public void IsKindEnabled_ReturnsDefaultValuesCorrectly(InboxItemKind kind, bool expected)
    {
        var settings = new NotificationSettings();
        Assert.Equal(expected, settings.IsKindEnabled(kind));
    }

    [Fact]
    public void IsKindEnabled_RespectsCustomValues()
    {
        var settings = new NotificationSettings
        {
            ReviewRequests = false,
            MentionsAndAssignments = false,
            CiStatus = true,
            WatchedRepoActivity = false,
            Participating = false
        };

        Assert.False(settings.IsKindEnabled(InboxItemKind.ReviewRequest));
        Assert.False(settings.IsKindEnabled(InboxItemKind.Mention));
        Assert.False(settings.IsKindEnabled(InboxItemKind.Assignment));
        Assert.True(settings.IsKindEnabled(InboxItemKind.CiStatus));
        Assert.False(settings.IsKindEnabled(InboxItemKind.RepoActivity));
        Assert.False(settings.IsKindEnabled(InboxItemKind.Participating));
    }
}
