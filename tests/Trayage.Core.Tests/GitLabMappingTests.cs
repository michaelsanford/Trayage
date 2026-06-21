using Trayage.Core.Models;
using Trayage.Core.Providers.GitLab;

namespace Trayage.Core.Tests;

public sealed class GitLabMappingTests
{
    [Theory]
    [InlineData("assigned", InboxItemKind.Assignment)]
    [InlineData("review_requested", InboxItemKind.ReviewRequest)]
    [InlineData("approval_required", InboxItemKind.ReviewRequest)]
    [InlineData("mentioned", InboxItemKind.Mention)]
    [InlineData("directly_addressed", InboxItemKind.Mention)]
    [InlineData("build_failed", InboxItemKind.CiStatus)]
    [InlineData("marked", InboxItemKind.Participating)]
    [InlineData("unmergeable", InboxItemKind.Participating)]
    [InlineData("merge_train_removed", InboxItemKind.Participating)]
    [InlineData("member_access_requested", InboxItemKind.Participating)]
    [InlineData(null, InboxItemKind.Participating)]
    public void ToKind_MapsActionNames(string? action, InboxItemKind expected)
    {
        Assert.Equal(expected, GitLabActionMapper.ToKind(action));
    }

    [Fact]
    public void ToInboxItem_MapsFieldsAndStableId()
    {
        var todo = new GitLabTodo
        {
            Id = 42,
            ActionName = "review_requested",
            TargetType = "MergeRequest",
            TargetUrl = "https://gitlab.com/acme/widgets/-/merge_requests/7",
            UpdatedAt = "2026-02-03T10:00:00.000Z",
            Project = new GitLabProject { PathWithNamespace = "acme/widgets" },
            Target = new GitLabTarget { Title = "Add widget" },
        };

        var item = GitLabMapping.ToInboxItem(todo);

        Assert.Equal("todo:42", item.Id);
        Assert.Equal(ProviderKind.GitLab, item.Provider);
        Assert.Equal(InboxItemKind.ReviewRequest, item.Kind);
        Assert.Equal("Add widget", item.Title);
        Assert.Equal("acme/widgets", item.RepositoryFullName);
        Assert.Equal("https://gitlab.com/acme/widgets/-/merge_requests/7", item.WebUrl);
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero), item.UpdatedAt);
        Assert.True(item.IsUnread);
    }

    [Fact]
    public void ToInboxItem_FallsBackToBodyThenPlaceholder_AndRepoHomeUrl()
    {
        var bodyOnly = new GitLabTodo
        {
            Id = 7,
            ActionName = "marked",
            Body = "Pipeline notice",
            Project = new GitLabProject { PathWithNamespace = "acme/widgets" },
        };

        var item = GitLabMapping.ToInboxItem(bodyOnly);

        // No target title → use body; no target_url → repo home page.
        Assert.Equal("Pipeline notice", item.Title);
        Assert.Equal("https://gitlab.com/acme/widgets", item.WebUrl);

        var empty = new GitLabTodo { Id = 9 };
        var fallback = GitLabMapping.ToInboxItem(empty);
        Assert.Equal("(no title)", fallback.Title);
        Assert.Equal("unknown/unknown", fallback.RepositoryFullName);
    }

    [Fact]
    public void ParseTimestamp_InvalidValue_ReturnsMinValue()
    {
        Assert.Equal(DateTimeOffset.MinValue, GitLabMapping.ParseTimestamp("not-a-date"));
        Assert.Equal(DateTimeOffset.MinValue, GitLabMapping.ParseTimestamp(null));
    }
}
