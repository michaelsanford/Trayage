using Trayage.Core.Models;

namespace Trayage.Core.Inbox;

/// <summary>Parameters that shape an inbox fetch, independent of any one provider.</summary>
/// <param name="WatchedRepositories">
/// "owner/repo" names the user wants to see <em>all</em> activity for, regardless of
/// whether they are personally involved.
/// </param>
public sealed record InboxQuery(IReadOnlyCollection<string> WatchedRepositories)
{
    public static readonly InboxQuery Empty = new(Array.Empty<string>());
}

/// <summary>
/// A source of inbox items for one service. Implementations own their own auth state;
/// the polling service only asks whether they are connected and pulls items.
/// </summary>
public interface IInboxProvider
{
    ProviderKind Provider { get; }

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
}
