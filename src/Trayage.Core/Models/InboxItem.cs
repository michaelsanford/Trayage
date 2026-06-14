namespace Trayage.Core.Models;

/// <summary>The source service an inbox item came from.</summary>
public enum ProviderKind
{
    GitHub,
    Bitbucket,
}

/// <summary>
/// The class of activity an item represents. Notification rules and the tray UI
/// group on this, and it maps onto provider-specific reasons during fetch.
/// </summary>
public enum InboxItemKind
{
    /// <summary>You have been requested to review a pull request.</summary>
    ReviewRequest,

    /// <summary>You were @-mentioned in an issue, PR, or comment.</summary>
    Mention,

    /// <summary>An issue or PR was assigned to you (or you authored it and it has new activity).</summary>
    Assignment,

    /// <summary>A build / check status changed on one of your pull requests.</summary>
    CiStatus,

    /// <summary>Any activity on a repository the user has explicitly chosen to watch.</summary>
    RepoActivity,
}

/// <summary>
/// A single, provider-agnostic entry in the unified inbox. Immutable; providers
/// translate their native payloads into these so the rest of the app never needs
/// to know GitHub from Bitbucket.
/// </summary>
public sealed record InboxItem
{
    /// <summary>Provider-stable identifier (e.g. a GitHub notification thread id).</summary>
    public required string Id { get; init; }

    public required ProviderKind Provider { get; init; }

    public required InboxItemKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>"owner/repo" form, used for grouping and watched-repo matching.</summary>
    public required string RepositoryFullName { get; init; }

    /// <summary>Human-readable reason this item is in your inbox (e.g. "review_requested").</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The page to open in the browser when the item is clicked.</summary>
    public required string WebUrl { get; init; }

    /// <summary>When the underlying thread last changed; drives "new vs. updated" detection.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsUnread { get; init; } = true;

    /// <summary>Stable cross-snapshot key: a given thread keeps the same key over time.</summary>
    public (ProviderKind, string) Key => (Provider, Id);
}
