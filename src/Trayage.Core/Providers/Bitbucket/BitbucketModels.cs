// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

using System.Text.Json.Serialization;

namespace Trayage.Core.Providers.Bitbucket;

/// <summary>Token response from Bitbucket's OAuth access_token endpoint.</summary>
public sealed class BitbucketTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    /// <summary>Space-separated scopes actually granted to this token (not PII).</summary>
    [JsonPropertyName("scopes")]
    public string? Scopes { get; set; }
}

/// <summary>Subset of GET /2.0/user.</summary>
public sealed class BitbucketUser
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

/// <summary>A page of pull requests (GET …/pullrequests).</summary>
public sealed class BitbucketPagedPullRequests
{
    [JsonPropertyName("values")]
    public List<BitbucketPullRequest> Values { get; set; } = new();

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

/// <summary>A page of the user's workspaces (GET /2.0/user/workspaces).</summary>
public sealed class BitbucketPagedUserWorkspaces
{
    [JsonPropertyName("values")]
    public List<BitbucketUserWorkspace> Values { get; set; } = new();

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

/// <summary>
/// One entry from the user's workspaces list. Tolerates both shapes Bitbucket may return:
/// the workspace fields inline, or nested under a <c>workspace</c> object (membership-style).
/// </summary>
public sealed class BitbucketUserWorkspace
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("workspace")]
    public BitbucketWorkspaceRef? Workspace { get; set; }

    public string? EffectiveSlug => string.IsNullOrEmpty(Slug) ? Workspace?.Slug : Slug;
}

/// <summary>Nested workspace reference carrying the addressing slug.</summary>
public sealed class BitbucketWorkspaceRef
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

/// <summary>A page of repositories (GET /2.0/repositories/{workspace}).</summary>
public sealed class BitbucketPagedRepositories
{
    [JsonPropertyName("values")]
    public List<BitbucketRepositorySummary> Values { get; set; } = new();

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

/// <summary>Subset of a repository object used to populate the watched-repo picker.</summary>
public sealed class BitbucketRepositorySummary
{
    /// <summary>"workspace/repo-slug" — matches <see cref="Models.InboxItem.RepositoryFullName"/>.</summary>
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("updated_on")]
    public string? UpdatedOn { get; set; }
}

public sealed class BitbucketPullRequest
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("updated_on")]
    public string? UpdatedOn { get; set; }

    [JsonPropertyName("links")]
    public BitbucketLinks? Links { get; set; }

    [JsonPropertyName("destination")]
    public BitbucketPullRequestEndpoint? Destination { get; set; }
}

public sealed class BitbucketPullRequestEndpoint
{
    [JsonPropertyName("repository")]
    public BitbucketRepository? Repository { get; set; }
}

public sealed class BitbucketRepository
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }
}

public sealed class BitbucketLinks
{
    [JsonPropertyName("html")]
    public BitbucketLink? Html { get; set; }
}

public sealed class BitbucketLink
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }
}
