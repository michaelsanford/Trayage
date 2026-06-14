using Trayage.Core.Inbox;
using Trayage.Core.Models;

namespace Trayage.Core.Tests;

public sealed class InboxDifferTests
{
    private readonly InboxDiffer _differ = new();

    [Fact]
    public void NewItem_IsReported()
    {
        var previous = new[] { TestData.Item("1") };
        var current = new[] { TestData.Item("1"), TestData.Item("2") };

        var result = _differ.FindNewOrUpdated(previous, current);

        Assert.Single(result);
        Assert.Equal("2", result[0].Id);
    }

    [Fact]
    public void UnchangedItem_IsNotReported()
    {
        var previous = new[] { TestData.Item("1") };
        var current = new[] { TestData.Item("1") };

        Assert.Empty(_differ.FindNewOrUpdated(previous, current));
    }

    [Fact]
    public void UpdatedTimestamp_IsReportedAsChanged()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var previous = new[] { TestData.Item("1", updatedAt: t0) };
        var current = new[] { TestData.Item("1", updatedAt: t0.AddMinutes(5)) };

        var result = _differ.FindNewOrUpdated(previous, current);

        Assert.Single(result);
        Assert.Equal("1", result[0].Id);
    }

    [Fact]
    public void SameIdDifferentProvider_IsTreatedAsDistinct()
    {
        var previous = new[] { TestData.Item("1", ProviderKind.GitHub) };
        var current = new[]
        {
            TestData.Item("1", ProviderKind.GitHub),
            TestData.Item("1", ProviderKind.Bitbucket),
        };

        var result = _differ.FindNewOrUpdated(previous, current);

        Assert.Single(result);
        Assert.Equal(ProviderKind.Bitbucket, result[0].Provider);
    }

    [Fact]
    public void EmptyPrevious_ReportsEverything()
    {
        var current = new[] { TestData.Item("1"), TestData.Item("2") };

        Assert.Equal(2, _differ.FindNewOrUpdated(Array.Empty<InboxItem>(), current).Count);
    }
}
