using Trayage.Core.Models;

namespace Trayage.Core.Providers.GitHub;

/// <summary>Maps a GitHub notification <c>reason</c> to a Trayage item kind.</summary>
public static class GitHubReasonMapper
{
    public static InboxItemKind ToKind(string? reason) => reason switch
    {
        "review_requested" => InboxItemKind.ReviewRequest,
        "mention" or "team_mention" => InboxItemKind.Mention,
        "assign" => InboxItemKind.Assignment,
        "ci_activity" => InboxItemKind.CiStatus,
        // author, comment, subscribed, state_change, manual, security_alert, … — these
        // are general involvement; treated as repo activity and only toasted for repos
        // the user has explicitly chosen to watch (see the notification rule engine).
        _ => InboxItemKind.RepoActivity,
    };
}

/// <summary>
/// Builds a browser-openable URL from a notification subject. GitHub returns the
/// subject as an <em>API</em> URL (api.github.com/repos/…); this translates it to the
/// matching web page, falling back to the repository home page when it can't.
/// </summary>
public static class GitHubWebUrl
{
    public static string Build(string? subjectApiUrl, string? subjectType, string repositoryHtmlUrl)
    {
        // CI notifications arrive as a CheckSuite subject with no URL; GitHub doesn't expose
        // the specific run in the notification, so point at the repository's Actions tab —
        // far more useful than the repository home page.
        if (subjectType == "CheckSuite")
        {
            return $"{repositoryHtmlUrl}/actions";
        }

        if (string.IsNullOrWhiteSpace(subjectApiUrl))
        {
            return repositoryHtmlUrl;
        }

        var lastSegment = subjectApiUrl.TrimEnd('/').Split('/').LastOrDefault();
        if (string.IsNullOrEmpty(lastSegment))
        {
            return repositoryHtmlUrl;
        }

        return subjectType switch
        {
            "PullRequest" => $"{repositoryHtmlUrl}/pull/{lastSegment}",
            "Issue" => $"{repositoryHtmlUrl}/issues/{lastSegment}",
            "Commit" => $"{repositoryHtmlUrl}/commit/{lastSegment}",
            "Release" => $"{repositoryHtmlUrl}/releases",
            "Discussion" => $"{repositoryHtmlUrl}/discussions",
            _ => repositoryHtmlUrl,
        };
    }
}
