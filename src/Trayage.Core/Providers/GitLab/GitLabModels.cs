// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

using System.Text.Json.Serialization;

namespace Trayage.Core.Providers.GitLab;

/// <summary>Response from <c>POST /oauth/authorize_device</c> (device flow step 1).</summary>
public sealed class GitLabDeviceAuthResponse
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; init; }

    [JsonPropertyName("user_code")]
    public string? UserCode { get; init; }

    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; init; }

    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }
}

/// <summary>
/// Response from <c>POST /oauth/token</c>. On the success path it carries the tokens; while
/// the user hasn't authorized yet it carries <c>error</c> = <c>authorization_pending</c> or
/// <c>slow_down</c> (the device-flow poll states).
/// </summary>
public sealed class GitLabTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

/// <summary>The authenticated user (<c>GET /api/v4/user</c>); only the username is used.</summary>
public sealed class GitLabUser
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

/// <summary>A single to-do from <c>GET /api/v4/todos</c>.</summary>
public sealed class GitLabTodo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("action_name")]
    public string? ActionName { get; init; }

    [JsonPropertyName("target_type")]
    public string? TargetType { get; init; }

    [JsonPropertyName("target_url")]
    public string? TargetUrl { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }

    [JsonPropertyName("project")]
    public GitLabProject? Project { get; init; }

    [JsonPropertyName("target")]
    public GitLabTarget? Target { get; init; }
}

public sealed class GitLabProject
{
    [JsonPropertyName("path_with_namespace")]
    public string? PathWithNamespace { get; init; }
}

public sealed class GitLabTarget
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}
