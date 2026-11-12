using System.Globalization;

namespace Trayage.Core.Providers;

/// <summary>Parses the ISO-8601 timestamps every provider API returns.</summary>
public static class ProviderTimestamp
{
    /// <summary>
    /// Parses <paramref name="value"/> as UTC (an offset-less value is assumed to be UTC), or
    /// returns <see cref="DateTimeOffset.MinValue"/> when it's missing or malformed so a bad
    /// field sorts to the bottom instead of failing the whole fetch.
    /// </summary>
    public static DateTimeOffset Parse(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
