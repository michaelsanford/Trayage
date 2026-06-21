using System.Globalization;
using Trayage.Core.Models;

namespace Trayage.Core.Providers.GitLab;

/// <summary>Maps a GitLab to-do <c>action_name</c> to a Trayage item kind.</summary>
public static class GitLabActionMapper
{
    public static InboxItemKind ToKind(string? action) => action switch
    {
        "assigned" => InboxItemKind.Assignment,
        "review_requested" or "approval_required" => InboxItemKind.ReviewRequest,
        "mentioned" or "directly_addressed" => InboxItemKind.Mention,
        "build_failed" => InboxItemKind.CiStatus,
        // marked, unmergeable, merge_train_removed, member_access_requested, … — general
        // involvement on something you author or take part in.
        _ => InboxItemKind.Participating,
    };
}

/// <summary>Translates a GitLab to-do into a provider-agnostic <see cref="InboxItem"/>.</summary>
public static class GitLabMapping
{
    public static InboxItem ToInboxItem(GitLabTodo todo)
    {
        var repo = todo.Project?.PathWithNamespace ?? "unknown/unknown";
        var title = !string.IsNullOrWhiteSpace(todo.Target?.Title)
            ? todo.Target!.Title!
            : !string.IsNullOrWhiteSpace(todo.Body)
                ? todo.Body!
                : "(no title)";

        return new InboxItem
        {
            Id = $"todo:{todo.Id}",
            Provider = ProviderKind.GitLab,
            Kind = GitLabActionMapper.ToKind(todo.ActionName),
            Title = title,
            RepositoryFullName = repo,
            Reason = todo.ActionName ?? string.Empty,
            WebUrl = todo.TargetUrl ?? $"https://gitlab.com/{repo}",
            UpdatedAt = ParseTimestamp(todo.UpdatedAt),
            // Every pending to-do is an open action item, so it always counts as unread.
            IsUnread = true,
        };
    }

    public static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
