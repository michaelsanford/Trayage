using Trayage.Core.Models;

namespace Trayage.Core.Configuration;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum NotificationStyle
{
    Both,
    ToastOnly,
    AudioOnly
}

/// <summary>
/// Which classes of new activity should raise a Windows toast. Watched-repo activity
/// is governed separately by each account's watched-repository list.
/// </summary>
public sealed class NotificationSettings
{
    public bool ReviewRequests { get; set; } = true;
    public bool MentionsAndAssignments { get; set; } = true;
    public bool CiStatus { get; set; }
    public bool WatchedRepoActivity { get; set; } = true;
    public NotificationStyle Style { get; set; } = NotificationStyle.Both;
    public string Sound { get; set; } = "System Asterisk";
    public int Volume { get; set; } = 50;

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

/// <summary>
/// Legacy single-account-per-provider connection state, superseded by
/// <see cref="ProviderAccount"/>. Retained only so a pre-accounts <c>settings.json</c> still
/// deserialises and can be migrated by <see cref="SettingsMigration"/>; nothing writes it.
/// </summary>
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
    /// <summary>
    /// Bumped whenever the persisted shape changes in a way that needs data migration.
    /// A file written before accounts existed has no such property, so it deserialises as 0
    /// and <see cref="SettingsMigration"/> knows to upgrade it.
    /// </summary>
    public int SchemaVersion { get; set; }

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

    public NotificationSettings Notifications { get; init; } = new();

    /// <summary>
    /// The connected accounts, across every provider. Several accounts may share a
    /// <see cref="ProviderKind"/>; each owns its own token and watched repositories.
    /// </summary>
    public List<ProviderAccount> Accounts { get; init; } = new();

    /// <summary>
    /// Legacy global watched-repo list, superseded by <see cref="ProviderAccount.WatchedRepositories"/>.
    /// Read once by <see cref="SettingsMigration"/>, then left alone.
    /// </summary>
    public List<string> WatchedRepositories { get; init; } = new();

    /// <summary>Legacy pre-accounts GitHub slot. See <see cref="SettingsMigration"/>.</summary>
    public ProviderConnectionState GitHub { get; init; } = new();

    /// <summary>Legacy pre-accounts Bitbucket slot. See <see cref="SettingsMigration"/>.</summary>
    public ProviderConnectionState Bitbucket { get; init; } = new();

    /// <summary>Legacy pre-accounts GitLab slot. See <see cref="SettingsMigration"/>.</summary>
    public ProviderConnectionState GitLab { get; init; } = new();

    /// <summary>
    /// Every watched repository across every account. Notification rules ask "is this item's
    /// repo watched?" — the item already came from the account that watches it, so the union
    /// is the right set to test against.
    /// </summary>
    public IReadOnlyCollection<string> AllWatchedRepositories =>
        Accounts.SelectMany(a => a.WatchedRepositories).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The account with this id, or null when it has been removed.</summary>
    public ProviderAccount? FindAccount(string accountId) =>
        Accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.Ordinal));

    /// <summary>
    /// Deep copy. Lets the settings store cache one canonical instance yet hand callers
    /// independent objects, so the common load → mutate → save pattern can't corrupt the cache.
    /// </summary>
    public TrayageSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
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
            Style = Notifications.Style,
            Sound = Notifications.Sound,
            Volume = Notifications.Volume,
        },
        Accounts = Accounts.Select(a => a.Clone()).ToList(),
        WatchedRepositories = new List<string>(WatchedRepositories),
        GitHub = new ProviderConnectionState { Connected = GitHub.Connected, AccountLogin = GitHub.AccountLogin },
        Bitbucket = new ProviderConnectionState { Connected = Bitbucket.Connected, AccountLogin = Bitbucket.AccountLogin },
        GitLab = new ProviderConnectionState { Connected = GitLab.Connected, AccountLogin = GitLab.AccountLogin },
    };
}
