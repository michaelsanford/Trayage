using Microsoft.Extensions.Logging.Abstractions;
using Trayage.Core.Configuration;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trayage-settings-{Guid.NewGuid():N}.json");

    private JsonSettingsStore NewStore() => new(NullLogger<JsonSettingsStore>.Instance, _path);

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var settings = NewStore().Load();

        Assert.Equal(300, settings.PollIntervalSeconds);
        Assert.True(settings.Notifications.ReviewRequests);
        Assert.True(settings.Notifications.Participating);
        Assert.True(settings.SurfaceRecentlyModified);
        Assert.Empty(settings.WatchedRepositories);
    }

    [Fact]
    public void Load_WhenNewFlagsAbsentFromJson_DefaultsToOn()
    {
        // Settings files written before these flags existed must opt the user in.
        File.WriteAllText(_path, "{\"PollIntervalSeconds\":120}");

        var settings = NewStore().Load();

        Assert.True(settings.SurfaceRecentlyModified);
        Assert.True(settings.Notifications.Participating);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsNewFlags()
    {
        var store = NewStore();
        var saved = new TrayageSettings { SurfaceRecentlyModified = false };
        saved.Notifications.Participating = false;

        store.Save(saved);
        var loaded = store.Load();

        Assert.False(loaded.SurfaceRecentlyModified);
        Assert.False(loaded.Notifications.Participating);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = NewStore();
        var saved = new TrayageSettings
        {
            PollIntervalSeconds = 120,
            Theme = AppTheme.Light,
            StartWithWindows = true,
            WatchedRepositories = { "octocat/hello-world", "acme/widgets" },
        };
        saved.Notifications.CiStatus = true;
        saved.GitHub.Connected = true;
        saved.GitHub.AccountLogin = "octocat";

        store.Save(saved);
        var loaded = store.Load();

        Assert.Equal(120, loaded.PollIntervalSeconds);
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.Notifications.CiStatus);
        Assert.True(loaded.GitHub.Connected);
        Assert.Equal("octocat", loaded.GitHub.AccountLogin);
        Assert.Equal(new[] { "octocat/hello-world", "acme/widgets" }, loaded.WatchedRepositories);
    }

    [Fact]
    public void Save_PersistsEnumsAsReadableStrings()
    {
        NewStore().Save(new TrayageSettings { Theme = AppTheme.Dark });

        Assert.Contains("\"Dark\"", File.ReadAllText(_path));
    }

    [Fact]
    public void Load_OnCorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_path, "{ this is not valid json");

        Assert.Equal(300, NewStore().Load().PollIntervalSeconds);
    }

    [Fact]
    public void Load_ReturnsIndependentInstances()
    {
        var store = NewStore();
        store.Save(new TrayageSettings { PollIntervalSeconds = 111, WatchedRepositories = { "a/b" } });

        var first = store.Load();
        first.PollIntervalSeconds = 999;
        first.WatchedRepositories.Add("x/y");
        first.GitHub.Connected = true;

        var second = store.Load();
        Assert.Equal(111, second.PollIntervalSeconds);
        Assert.Equal(new[] { "a/b" }, second.WatchedRepositories);
        Assert.False(second.GitHub.Connected);
    }

    [Fact]
    public void Load_ServesFromCache_WhenTimestampUnchanged()
    {
        var store = NewStore();
        store.Save(new TrayageSettings { PollIntervalSeconds = 111 });
        var stamp = File.GetLastWriteTimeUtc(_path);

        Assert.Equal(111, store.Load().PollIntervalSeconds);

        // Change the content but restore the original timestamp: a cache keyed on mtime
        // must return the previously parsed value rather than re-reading the file.
        File.WriteAllText(_path, "{\"PollIntervalSeconds\":999}");
        File.SetLastWriteTimeUtc(_path, stamp);

        Assert.Equal(111, store.Load().PollIntervalSeconds);
    }

    [Fact]
    public void Load_Reparses_WhenTimestampChanges()
    {
        var store = NewStore();
        store.Save(new TrayageSettings { PollIntervalSeconds = 111 });
        Assert.Equal(111, store.Load().PollIntervalSeconds);

        File.WriteAllText(_path, "{\"PollIntervalSeconds\":222}");
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(222, store.Load().PollIntervalSeconds);
    }

    [Fact]
    public void Load_AfterSave_ReflectsSavedValues()
    {
        var store = NewStore();
        store.Save(new TrayageSettings { PollIntervalSeconds = 111 });
        Assert.Equal(111, store.Load().PollIntervalSeconds);

        store.Save(new TrayageSettings { PollIntervalSeconds = 222 });
        Assert.Equal(222, store.Load().PollIntervalSeconds);
    }

    [Fact]
    public void IsKindEnabled_MapsKindsToToggles()
    {
        var n = new NotificationSettings { ReviewRequests = true, CiStatus = false, MentionsAndAssignments = true };

        Assert.True(n.IsKindEnabled(InboxItemKind.ReviewRequest));
        Assert.True(n.IsKindEnabled(InboxItemKind.Assignment));
        Assert.False(n.IsKindEnabled(InboxItemKind.CiStatus));
        Assert.True(n.IsKindEnabled(InboxItemKind.Participating));

        Assert.False(new NotificationSettings { Participating = false }.IsKindEnabled(InboxItemKind.Participating));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
