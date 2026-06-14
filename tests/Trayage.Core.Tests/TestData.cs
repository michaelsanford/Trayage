using Trayage.Core.Models;

namespace Trayage.Core.Tests;

/// <summary>Convenience factory so tests can spin up inbox items with minimal noise.</summary>
internal static class TestData
{
    public static InboxItem Item(
        string id,
        ProviderKind provider = ProviderKind.GitHub,
        InboxItemKind kind = InboxItemKind.ReviewRequest,
        string repo = "octocat/hello-world",
        DateTimeOffset? updatedAt = null,
        bool unread = true) => new()
        {
            Id = id,
            Provider = provider,
            Kind = kind,
            Title = $"Item {id}",
            RepositoryFullName = repo,
            Reason = kind.ToString(),
            WebUrl = $"https://example.test/{provider}/{id}",
            UpdatedAt = updatedAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            IsUnread = unread,
        };
}
