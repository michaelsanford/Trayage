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

        Assert.Equal(60, settings.PollIntervalSeconds);
        Assert.True(settings.Notifications.ReviewRequests);
        Assert.Empty(settings.WatchedRepositories);
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

        Assert.Equal(60, NewStore().Load().PollIntervalSeconds);
    }

    [Fact]
    public void IsKindEnabled_MapsKindsToToggles()
    {
        var n = new NotificationSettings { ReviewRequests = true, CiStatus = false, MentionsAndAssignments = true };

        Assert.True(n.IsKindEnabled(InboxItemKind.ReviewRequest));
        Assert.True(n.IsKindEnabled(InboxItemKind.Assignment));
        Assert.False(n.IsKindEnabled(InboxItemKind.CiStatus));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
