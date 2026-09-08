using Trayage.Core.Configuration;
using Trayage.Core.Inbox;

namespace Trayage.Core.Tests;

public sealed class InboxRecencyTests
{
    [Fact]
    public void WindowFor_WhenEnabled_IsTwicePollInterval()
    {
        var settings = new TrayageSettings { SurfaceRecentlyModified = true, PollIntervalSeconds = 900 };

        Assert.Equal(TimeSpan.FromSeconds(1800), InboxRecency.WindowFor(settings));
    }

    [Fact]
    public void WindowFor_WhenDisabled_IsNull()
    {
        var settings = new TrayageSettings { SurfaceRecentlyModified = false, PollIntervalSeconds = 900 };

        Assert.Null(InboxRecency.WindowFor(settings));
    }

    [Fact]
    public void WindowFor_GuardsAgainstNonPositivePollInterval()
    {
        var settings = new TrayageSettings { SurfaceRecentlyModified = true, PollIntervalSeconds = 0 };

        Assert.Equal(TimeSpan.FromSeconds(2), InboxRecency.WindowFor(settings));
    }

    [Fact]
    public void IsRecent_JustInsideWindow_IsTrue()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = TestData.Item("1", updatedAt: updatedAt);

        Assert.True(InboxRecency.IsRecent(item, updatedAt.AddMinutes(29), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void IsRecent_JustOutsideWindow_IsFalse()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = TestData.Item("1", updatedAt: updatedAt);

        Assert.False(InboxRecency.IsRecent(item, updatedAt.AddMinutes(31), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void IsRecent_NullWindow_IsAlwaysFalse()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = TestData.Item("1", updatedAt: updatedAt);

        Assert.False(InboxRecency.IsRecent(item, updatedAt, null));
    }

    [Fact]
    public void ShouldSurfaceRead_RecentProviderRead_IsTrue()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = TestData.Item("1", updatedAt: updatedAt, unread: false);

        Assert.True(InboxRecency.ShouldSurfaceRead(item, updatedAt.AddMinutes(1), TimeSpan.FromMinutes(30)));
    }

    /// <summary>
    /// The window exists to distrust a *provider's* read flag for a while. It must not distrust
    /// the user: without this, an item they just marked read would sit on screen for ~2× the poll
    /// interval and the click would look like it did nothing.
    /// </summary>
    [Fact]
    public void ShouldSurfaceRead_WhenTheUserMarkedItRead_IsFalseEvenInsideTheWindow()
    {
        var updatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = TestData.Item("1", updatedAt: updatedAt, unread: false) with { IsExplicitlyRead = true };

        Assert.False(InboxRecency.ShouldSurfaceRead(item, updatedAt.AddMinutes(1), TimeSpan.FromMinutes(30)));
        // The underlying recency answer is unchanged; only the surfacing decision differs.
        Assert.True(InboxRecency.IsRecent(item, updatedAt.AddMinutes(1), TimeSpan.FromMinutes(30)));
    }
}
