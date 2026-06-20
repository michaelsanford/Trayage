namespace Trayage.Core.Inbox;

/// <summary>
/// Parses user-entered repository references into the canonical "owner/repo" form used
/// throughout the inbox (and as the watched-repo key). Accepts a bare "owner/repo" or a
/// pasted web URL (e.g. https://bitbucket.org/owner/repo/pull-requests/1), tolerating a
/// trailing ".git", query string, or fragment. Returns null when the input can't be
/// reduced to an owner and a repo.
/// </summary>
public static class RepositoryReference
{
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var value = input.Trim();

        // A pasted URL (with scheme, or a bare host) — keep only the path after the host.
        if (value.Contains("://", StringComparison.Ordinal) ||
            value.StartsWith("bitbucket.org/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = StripHost(value);
        }

        // Drop any query/fragment and a trailing .git, then the leading/trailing slashes.
        var cut = value.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
        {
            value = value[..cut];
        }

        value = value.Trim('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var owner = parts[0];
        var repo = parts[1];
        if (owner.Length == 0 || repo.Length == 0 || owner.Contains(' ') || repo.Contains(' '))
        {
            return null;
        }

        return $"{owner}/{repo}";
    }

    private static string StripHost(string value)
    {
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            value = value[(scheme + 3)..];
        }

        var slash = value.IndexOf('/');
        return slash >= 0 ? value[(slash + 1)..] : string.Empty;
    }
}
