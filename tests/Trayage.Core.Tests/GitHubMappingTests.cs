using Trayage.Core.Models;
using Trayage.Core.Providers.GitHub;

namespace Trayage.Core.Tests;

public sealed class GitHubMappingTests
{
    [Theory]
    [InlineData("review_requested", InboxItemKind.ReviewRequest)]
    [InlineData("mention", InboxItemKind.Mention)]
    [InlineData("team_mention", InboxItemKind.Mention)]
    [InlineData("assign", InboxItemKind.Assignment)]
    [InlineData("ci_activity", InboxItemKind.CiStatus)]
    [InlineData("author", InboxItemKind.RepoActivity)]
    [InlineData("subscribed", InboxItemKind.RepoActivity)]
    [InlineData(null, InboxItemKind.RepoActivity)]
    public void ToKind_MapsReasons(string? reason, InboxItemKind expected)
    {
        Assert.Equal(expected, GitHubReasonMapper.ToKind(reason));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/o/r/pulls/123", "PullRequest", "https://github.com/o/r/pull/123")]
    [InlineData("https://api.github.com/repos/o/r/issues/45", "Issue", "https://github.com/o/r/issues/45")]
    [InlineData("https://api.github.com/repos/o/r/commits/abc123", "Commit", "https://github.com/o/r/commit/abc123")]
    public void Build_TranslatesApiUrlToWebUrl(string apiUrl, string type, string expected)
    {
        Assert.Equal(expected, GitHubWebUrl.Build(apiUrl, type, "https://github.com/o/r"));
    }

    [Fact]
    public void Build_NullSubjectUrl_FallsBackToRepoHome()
    {
        Assert.Equal("https://github.com/o/r", GitHubWebUrl.Build(null, "PullRequest", "https://github.com/o/r"));
    }

    [Fact]
    public void Build_UnknownType_FallsBackToRepoHome()
    {
        Assert.Equal("https://github.com/o/r",
            GitHubWebUrl.Build("https://api.github.com/repos/o/r/something/9", "CheckSuite", "https://github.com/o/r"));
    }
}
