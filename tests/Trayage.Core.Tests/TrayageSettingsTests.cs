using Trayage.Core.Configuration;

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

        var clone = original.Clone();

        Assert.Equal(120, clone.PollIntervalSeconds);
        Assert.Equal(AppTheme.Dark, clone.Theme);
        Assert.True(clone.StartWithWindows);
        Assert.False(clone.SurfaceRecentlyModified);
    }

    [Fact]
    public void Clone_DeepCopies_MutatingCloneLeavesOriginalUntouched()
    {
        var original = new TrayageSettings { WatchedRepositories = { "a/b" } };
        original.Notifications.ReviewRequests = true;
        original.GitHub.Connected = true;
        original.GitHub.AccountLogin = "octocat";
        original.Bitbucket.AccountLogin = "stelvio";

        var clone = original.Clone();
        clone.WatchedRepositories.Add("x/y");
        clone.Notifications.ReviewRequests = false;
        clone.GitHub.Connected = false;
        clone.GitHub.AccountLogin = "changed";
        clone.Bitbucket.AccountLogin = "changed";

        Assert.Equal(new[] { "a/b" }, original.WatchedRepositories);
        Assert.True(original.Notifications.ReviewRequests);
        Assert.True(original.GitHub.Connected);
        Assert.Equal("octocat", original.GitHub.AccountLogin);
        Assert.Equal("stelvio", original.Bitbucket.AccountLogin);
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
    }
}
