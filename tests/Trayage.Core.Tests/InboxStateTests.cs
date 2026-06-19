using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

public sealed class InboxStateTests
{
    [Fact]
    public void Set_ReplacesItems_AndUpdatesUnreadCounts()
    {
        var state = new InboxState();

        state.Set(new[]
        {
            TestData.Item("1", unread: true),
            TestData.Item("2", unread: true),
            TestData.Item("3", unread: false),
        });

        Assert.Equal(3, state.Items.Count);
        Assert.Equal(2, state.UnreadCount);
        Assert.True(state.HasUnread);
    }

    [Fact]
    public void Set_RaisesChangedExactlyOnce()
    {
        var state = new InboxState();
        var raised = 0;
        state.Changed += (_, _) => raised++;

        state.Set(new[] { TestData.Item("1") });

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Set_Null_YieldsEmptyNonNullList()
    {
        var state = new InboxState();
        state.Set(new[] { TestData.Item("1") });

        state.Set(null!);

        Assert.NotNull(state.Items);
        Assert.Empty(state.Items);
        Assert.False(state.HasUnread);
        Assert.Equal(0, state.UnreadCount);
    }
}
