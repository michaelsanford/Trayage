using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Trayage.Core.Inbox;

namespace Trayage.App;

/// <summary>Returns the logical negation of a boolean.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false;
}

/// <summary>Maps <c>false</c> to Visible and <c>true</c> to Collapsed.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}

/// <summary>Collapses an element when its bound string is null or empty.</summary>
public sealed class EmptyStringToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Projects one half of an inbox group header so the two can be styled differently — owner
/// de-emphasised, repository name emphasised — from the single string the collection view
/// hands us. <c>ConverterParameter</c> picks the half: "Owner" (which keeps its trailing
/// slash, so the two runs read as one path) or anything else for the name. A header with no
/// slash — the "Today"/"Yesterday" recency buckets, or a bare owner when grouping by owner —
/// yields an empty owner and the whole label as the name, rendering as a single label.
/// </summary>
public sealed class RepoSegmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (owner, name) = RepositoryReference.Split(value as string);
        if (!string.Equals(parameter as string, "Owner", StringComparison.Ordinal))
        {
            return name;
        }

        return owner.Length == 0 ? string.Empty : owner + "/";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
