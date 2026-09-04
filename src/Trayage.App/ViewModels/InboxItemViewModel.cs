using Trayage.Core.Models;
using Wpf.Ui.Controls;

namespace Trayage.App.ViewModels;

/// <summary>Read-only presentation wrapper around an <see cref="InboxItem"/> for the flyout list.</summary>
public sealed class InboxItemViewModel
{
    private readonly bool _includeRepoInSubtitle;

    /// <param name="item">The inbox item.</param>
    /// <param name="includeRepoInSubtitle">
    /// True when the list is flat (sequential), so the repository name is shown in the
    /// subtitle; false when grouped by repository (the group header already shows it).
    /// </param>
    /// <param name="accountLabel">
    /// Which account this item arrived through, shown only when more than one account is
    /// connected to the same provider; null otherwise, so single-account users see no change.
    /// </param>
    public InboxItemViewModel(InboxItem item, bool includeRepoInSubtitle = false, string? accountLabel = null)
    {
        Item = item;
        _includeRepoInSubtitle = includeRepoInSubtitle;
        AccountLabel = accountLabel;
    }

    /// <summary>The account this item came from, or null when naming it wouldn't disambiguate.</summary>
    public string? AccountLabel { get; }

    public InboxItem Item { get; }

    public ProviderKind Provider => Item.Provider;

    public string Title => Item.Title;

    public string RepositoryFullName => Item.RepositoryFullName;

    public string WebUrl => Item.WebUrl;

    public bool IsUnread => Item.IsUnread;

    public DateTimeOffset UpdatedAt => Item.UpdatedAt;



    /// <summary>
    /// Coarse recency bucket used to group the flat (non-repo) list. Returned in newest-first
    /// order naturally because the view also sorts by <see cref="UpdatedAt"/> descending.
    /// </summary>
    public string TimeBucket
    {
        get
        {
            var today = DateTimeOffset.Now.Date;
            var when = Item.UpdatedAt.ToLocalTime().Date;
            var days = (today - when).Days;
            return days switch
            {
                <= 0 => "Today",
                1 => "Yesterday",
                < 7 => "Earlier this week",
                _ => "Older",
            };
        }
    }

    public string KindLabel => Item.Kind switch
    {
        InboxItemKind.ReviewRequest => "Review requested",
        InboxItemKind.Mention => "Mention",
        InboxItemKind.Assignment => "Assigned",
        InboxItemKind.CiStatus => "CI status",
        InboxItemKind.RepoActivity => "Activity",
        InboxItemKind.Participating => "Participating",
        _ => Item.Kind.ToString(),
    };

    /// <summary>Per-kind glyph shown in the row badge.</summary>
    public SymbolRegular KindIcon => Item.Kind switch
    {
        InboxItemKind.ReviewRequest => SymbolRegular.BranchFork24,
        InboxItemKind.Mention => SymbolRegular.Mention24,
        InboxItemKind.Assignment => SymbolRegular.PersonTag24,
        InboxItemKind.CiStatus => SymbolRegular.CheckmarkCircle24,
        InboxItemKind.RepoActivity => SymbolRegular.Eye24,
        InboxItemKind.Participating => SymbolRegular.Comment24,
        _ => SymbolRegular.Circle24,
    };

    public string RelativeTime => FormatRelative(Item.UpdatedAt);

    public string Subtitle
    {
        get
        {
            var parts = new List<string>(4);
            if (_includeRepoInSubtitle)
            {
                parts.Add(RepositoryFullName);
            }

            parts.Add(KindLabel);
            if (AccountLabel is { Length: > 0 } account)
            {
                parts.Add(account);
            }

            parts.Add(RelativeTime);
            return string.Join(" · ", parts);
        }
    }

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when.ToUniversalTime();
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return "just now";
        }

        if (delta.TotalHours < 1)
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta.TotalDays < 1)
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        if (delta.TotalDays < 30)
        {
            return $"{(int)delta.TotalDays}d ago";
        }

        return when.ToLocalTime().ToString("d MMM");
    }
}
