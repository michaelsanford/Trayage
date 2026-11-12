using Trayage.Core.Providers;

namespace Trayage.Core.Tests;

public sealed class ProviderTimestampTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void Parse_MissingOrInvalid_ReturnsMinValue(string? value)
    {
        Assert.Equal(DateTimeOffset.MinValue, ProviderTimestamp.Parse(value));
    }

    [Fact]
    public void Parse_WithOffset_NormalisesToUtc()
    {
        var parsed = ProviderTimestamp.Parse("2026-03-01T12:00:00+02:00");

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), parsed);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }

    [Fact]
    public void Parse_WithoutOffset_AssumesUtc()
    {
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero), ProviderTimestamp.Parse("2026-03-01T12:00:00"));
    }
}
