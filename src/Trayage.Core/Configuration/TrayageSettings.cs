using Trayage.Core.Models;

namespace Trayage.Core.Configuration;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Which classes of new activity should raise a Windows toast. Watched-repo activity
/// is governed separately by <see cref="TrayageSettings.WatchedRepositories"/>.
/// </summary>
public sealed class NotificationSettings
{
    public bool ReviewRequests { get; set; } = true;
    public bool MentionsAndAssignments { get; set; } = true;
    public bool CiStatus { get; set; }
    public bool WatchedRepoActivity { get; set; } = true;

    /// <summary>Activity on issues/PRs you authored or are participating in.</summary>
    public bool Participating { get; set; } = true;

    /// <summary>Maps an item kind to its corresponding per-class toggle.</summary>
    public bool IsKindEnabled(InboxItemKind kind) => kind switch
    {
        InboxItemKind.ReviewRequest => ReviewRequests,
        InboxItemKind.Mention => MentionsAndAssignments,
        InboxItemKind.Assignment => MentionsAndAssignments,
        InboxItemKind.CiStatus => CiStatus,
        InboxItemKind.RepoActivity => WatchedRepoActivity,
        InboxItemKind.Participating => Participating,
        _ => false,
    };
}

/// <summary>Non-secret connection state for a provider. Tokens live in the secret store.</summary>
public sealed class ProviderConnectionState
{
    public bool Connected { get; set; }

    /// <summary>The signed-in account login/username, shown in Settings.</summary>
    public string? AccountLogin { get; set; }
}

/// <summary>
/// The full, serialisable application configuration. Deliberately holds <em>no</em>
/// secrets — access/refresh tokens are kept separately and encrypted via ISecretStore.
/// </summary>
public sealed class TrayageSettings
{
    public int PollIntervalSeconds { get; set; } = 300;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool StartWithWindows { get; set; }

    /// <summary>Set after the first launch so the welcome flyout is shown only once.</summary>
    public bool FirstRunCompleted { get; set; }

    /// <summary>When true, the file logger captures Debug-level detail (applies on next launch).</summary>
    public bool VerboseLogging { get; set; }

    /// <summary>When true, the inbox flyout groups items by repository; otherwise a flat, newest-first list.</summary>
    public bool GroupByRepository { get; set; } = true;

    /// <summary>When true, the inbox flyout shows read items (de-emphasised); otherwise only unread items appear.</summary>
    public bool ShowReadItems { get; set; } = true;

    /// <summary>
    /// When true, a read item is still surfaced in the list and eligible for a toast if it was updated
    /// within ~2× the poll interval. Bridges GitHub's web-vs-REST read-state desync (a thread the web
    /// "bell" still shows as new can already read <c>unread:false</c> over the REST API Trayage uses).
    /// When false, read items behave as before.
    /// </summary>
    public bool SurfaceRecentlyModified { get; set; } = true;

    public NotificationSettings Notifications { get; set; } = new();

    /// <summary>"owner/repo" names to surface and toast on for all activity.</summary>
    public List<string> WatchedRepositories { get; set; } = new();

    public ProviderConnectionState GitHub { get; set; } = new();

    public ProviderConnectionState Bitbucket { get; set; } = new();

    public ProviderConnectionState GitLab { get; set; } = new();

    /// <summary>
    /// Deep copy. Lets the settings store cache one canonical instance yet hand callers
    /// independent objects, so the common load → mutate → save pattern can't corrupt the cache.
    /// </summary>
    public TrayageSettings Clone() => new()
    {
        PollIntervalSeconds = PollIntervalSeconds,
        Theme = Theme,
        StartWithWindows = StartWithWindows,
        FirstRunCompleted = FirstRunCompleted,
        VerboseLogging = VerboseLogging,
        GroupByRepository = GroupByRepository,
        ShowReadItems = ShowReadItems,
        SurfaceRecentlyModified = SurfaceRecentlyModified,
        Notifications = new NotificationSettings
        {
            ReviewRequests = Notifications.ReviewRequests,
            MentionsAndAssignments = Notifications.MentionsAndAssignments,
            CiStatus = Notifications.CiStatus,
            WatchedRepoActivity = Notifications.WatchedRepoActivity,
            Participating = Notifications.Participating,
        },
        WatchedRepositories = new List<string>(WatchedRepositories),
        GitHub = new ProviderConnectionState { Connected = GitHub.Connected, AccountLogin = GitHub.AccountLogin },
        Bitbucket = new ProviderConnectionState { Connected = Bitbucket.Connected, AccountLogin = Bitbucket.AccountLogin },
        GitLab = new ProviderConnectionState { Connected = GitLab.Connected, AccountLogin = GitLab.AccountLogin },
    };
}
