using Trayage.Core.Models;
using Trayage.Core.Providers.Bitbucket;

namespace Trayage.Core.Tests;

public sealed class BitbucketMappingTests
{
    [Fact]
    public void ToInboxItem_UsesHtmlLinkAndStableId()
    {
        var pr = new BitbucketPullRequest
        {
            Id = 42,
            Title = "Add widget",
            UpdatedOn = "2026-02-03T10:00:00.000000+00:00",
            Links = new BitbucketLinks { Html = new BitbucketLink { Href = "https://bitbucket.org/acme/widgets/pull-requests/42" } },
            Destination = new BitbucketPullRequestEndpoint { Repository = new BitbucketRepository { FullName = "acme/widgets" } },
        };

        var item = BitbucketMapping.ToInboxItem(pr, InboxItemKind.ReviewRequest, "acme/widgets");

        Assert.Equal("pr:acme/widgets:42", item.Id);
        Assert.Equal(ProviderKind.Bitbucket, item.Provider);
        Assert.Equal(InboxItemKind.ReviewRequest, item.Kind);
        Assert.Equal("Add widget", item.Title);
        Assert.Equal("acme/widgets", item.RepositoryFullName);
        Assert.Equal("https://bitbucket.org/acme/widgets/pull-requests/42", item.WebUrl);
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero), item.UpdatedAt);
    }

    [Fact]
    public void ToInboxItem_FallsBackToConstructedUrlAndRepo()
    {
        var pr = new BitbucketPullRequest { Id = 7, Title = null, Links = null, Destination = null };

        var item = BitbucketMapping.ToInboxItem(pr, InboxItemKind.RepoActivity, "acme/widgets");

        Assert.Equal("acme/widgets", item.RepositoryFullName);
        Assert.Equal("https://bitbucket.org/acme/widgets/pull-requests/7", item.WebUrl);
        Assert.Equal("Pull request #7", item.Title);
    }

    [Fact]
    public void ParseTimestamp_InvalidValue_ReturnsMinValue()
    {
        Assert.Equal(DateTimeOffset.MinValue, BitbucketMapping.ParseTimestamp("not-a-date"));
        Assert.Equal(DateTimeOffset.MinValue, BitbucketMapping.ParseTimestamp(null));
    }
}
