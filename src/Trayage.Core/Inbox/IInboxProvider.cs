using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>Parameters that shape an inbox fetch, independent of any one provider.</summary>
/// <param name="WatchedRepositories">
/// "owner/repo" names the user wants to see <em>all</em> activity for, regardless of
/// whether they are personally involved. Scoped to the account being fetched — a repository
/// only one account can reach must not be queried with another's token.
/// </param>
public sealed record InboxQuery(IReadOnlyCollection<string> WatchedRepositories)
{
    public static readonly InboxQuery Empty = new(Array.Empty<string>());
}

/// <summary>How far a read mark got on the provider's side.</summary>
public enum MarkAsReadOutcome
{
    /// <summary>The service was told, and its own inbox now agrees.</summary>
    Propagated,

    /// <summary>
    /// This provider has no way to record a read mark — Bitbucket Cloud has no notification
    /// inbox — or isn't currently connected. An ordinary outcome, not a failure.
    /// </summary>
    NotSupported,

    /// <summary>
    /// The service refused the write because the account's token predates the write scope
    /// Trayage now asks for. Reconnecting the account fixes it; the local mark stands meanwhile.
    /// </summary>
    NeedsReauthorization,
}

/// <summary>
/// A source of inbox items for one <em>account</em> on one service. Implementations own their
/// own auth state; the polling service only asks whether they are connected and pulls items.
/// Several instances may share a <see cref="Provider"/>, so <see cref="AccountId"/> — not
/// <see cref="Provider"/> — identifies an instance.
/// </summary>
public interface IInboxProvider
{
    ProviderKind Provider { get; }

    /// <summary>Identifies this instance's account; matches <c>ProviderAccount.Id</c>.</summary>
    string AccountId { get; }

    /// <summary>The signed-in login/username, when known.</summary>
    string? AccountLogin { get; }

    /// <summary>Human-readable identification for toasts and errors (e.g. "GitHub · Work").</summary>
    string DisplayLabel { get; }

    /// <summary>True when the provider holds a usable, authenticated session.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Provider's preferred minimum polling cadence (e.g. honouring GitHub's
    /// <c>X-Poll-Interval</c>). Null means "use the app default".
    /// </summary>
    TimeSpan? SuggestedPollInterval { get; }

    /// <summary>
    /// Fetches the current inbox. Should return an empty list (not throw) when the
    /// provider is not connected. May throw on transient network/API failures, which
    /// the polling service is expected to catch and surface.
    /// </summary>
    Task<IReadOnlyList<InboxItem>> FetchInboxAsync(InboxQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the service this item has been read. Anything other than
    /// <see cref="MarkAsReadOutcome.Propagated"/> is an expected outcome rather than a failure:
    /// Trayage records the mark locally either way, so callers never branch on the provider.
    /// May throw on transient network/API failures, which callers are expected to catch.
    /// </summary>
    Task<MarkAsReadOutcome> TryMarkAsReadAsync(InboxItem item, CancellationToken cancellationToken);
}
