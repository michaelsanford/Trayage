using Trayage.Core.Models;

namespace Trayage.Core.Providers.Bitbucket;

/// <summary>Pure translation of Bitbucket pull requests into <see cref="InboxItem"/>s.</summary>
public static class BitbucketMapping
{
    public static InboxItem ToInboxItem(BitbucketPullRequest pr, InboxItemKind kind, string repositoryFullName, string accountId)
    {
        var repo = pr.Destination?.Repository?.FullName ?? repositoryFullName;
        var webUrl = pr.Links?.Html?.Href ?? $"https://bitbucket.org/{repo}/pull-requests/{pr.Id}";

        return new InboxItem
        {
            // Stable across polls and unique per repo+PR.
            Id = $"pr:{repo}:{pr.Id}",
            Provider = ProviderKind.Bitbucket,
            AccountId = accountId,
            Kind = kind,
            Title = pr.Title ?? $"Pull request #{pr.Id}",
            RepositoryFullName = repo,
            Reason = kind.ToString(),
            WebUrl = webUrl,
            UpdatedAt = ProviderTimestamp.Parse(pr.UpdatedOn),
            IsUnread = true,
        };
    }

    /// <summary>
    /// How an open pull request relates to the signed-in user: a review request when they're
    /// a reviewer, an assignment when they authored it, otherwise general repository activity.
    /// Review requests win, since that's the item that's waiting on them.
    /// </summary>
    public static InboxItemKind Classify(BitbucketPullRequest pr, string userUuid)
    {
        if (pr.Reviewers?.Any(r => IsUser(r, userUuid)) == true)
        {
            return InboxItemKind.ReviewRequest;
        }

        return IsUser(pr.Author, userUuid) ? InboxItemKind.Assignment : InboxItemKind.RepoActivity;
    }

    // Bitbucket UUIDs are braced GUIDs ("{…}"); compare case-insensitively to be safe.
    private static bool IsUser(BitbucketUser? user, string userUuid) =>
        string.Equals(user?.Uuid, userUuid, StringComparison.OrdinalIgnoreCase);
}
