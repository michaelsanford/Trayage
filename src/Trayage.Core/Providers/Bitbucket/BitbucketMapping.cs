using System.Globalization;
using Trayage.Core.Models;

namespace Trayage.Core.Providers.Bitbucket;

/// <summary>Pure translation of Bitbucket pull requests into <see cref="InboxItem"/>s.</summary>
public static class BitbucketMapping
{
    public static InboxItem ToInboxItem(BitbucketPullRequest pr, InboxItemKind kind, string repositoryFullName)
    {
        var repo = pr.Destination?.Repository?.FullName ?? repositoryFullName;
        var webUrl = pr.Links?.Html?.Href ?? $"https://bitbucket.org/{repo}/pull-requests/{pr.Id}";

        return new InboxItem
        {
            // Stable across polls and unique per repo+PR.
            Id = $"pr:{repo}:{pr.Id}",
            Provider = ProviderKind.Bitbucket,
            Kind = kind,
            Title = pr.Title ?? $"Pull request #{pr.Id}",
            RepositoryFullName = repo,
            Reason = kind.ToString(),
            WebUrl = webUrl,
            UpdatedAt = ParseTimestamp(pr.UpdatedOn),
            IsUnread = true,
        };
    }

    public static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
