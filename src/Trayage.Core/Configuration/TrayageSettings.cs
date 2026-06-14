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

    /// <summary>Maps an item kind to its corresponding per-class toggle.</summary>
    public bool IsKindEnabled(InboxItemKind kind) => kind switch
    {
        InboxItemKind.ReviewRequest => ReviewRequests,
        InboxItemKind.Mention => MentionsAndAssignments,
        InboxItemKind.Assignment => MentionsAndAssignments,
        InboxItemKind.CiStatus => CiStatus,
        InboxItemKind.RepoActivity => WatchedRepoActivity,
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
    public int PollIntervalSeconds { get; set; } = 60;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public bool StartWithWindows { get; set; }

    /// <summary>Set after the first launch so the welcome flyout is shown only once.</summary>
    public bool FirstRunCompleted { get; set; }

    /// <summary>When true, the file logger captures Debug-level detail (applies on next launch).</summary>
    public bool VerboseLogging { get; set; }

    public NotificationSettings Notifications { get; set; } = new();

    /// <summary>"owner/repo" names to surface and toast on for all activity.</summary>
    public List<string> WatchedRepositories { get; set; } = new();

    public ProviderConnectionState GitHub { get; set; } = new();

    public ProviderConnectionState Bitbucket { get; set; } = new();
}
