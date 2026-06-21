using System.Text.Json.Serialization;

namespace Trayage.Core.Providers.GitLab;

/// <summary>Response from <c>POST /oauth/authorize_device</c> (device flow step 1).</summary>
public sealed class GitLabDeviceAuthResponse
{
    [JsonPropertyName("device_code")]
    public string? DeviceCode { get; set; }

    [JsonPropertyName("user_code")]
    public string? UserCode { get; set; }

    [JsonPropertyName("verification_uri")]
    public string? VerificationUri { get; set; }

    [JsonPropertyName("verification_uri_complete")]
    public string? VerificationUriComplete { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }
}

/// <summary>
/// Response from <c>POST /oauth/token</c>. On the success path it carries the tokens; while
/// the user hasn't authorized yet it carries <c>error</c> = <c>authorization_pending</c> or
/// <c>slow_down</c> (the device-flow poll states).
/// </summary>
public sealed class GitLabTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>The authenticated user (<c>GET /api/v4/user</c>); only the username is used.</summary>
public sealed class GitLabUser
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}

/// <summary>A single to-do from <c>GET /api/v4/todos</c>.</summary>
public sealed class GitLabTodo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("action_name")]
    public string? ActionName { get; set; }

    [JsonPropertyName("target_type")]
    public string? TargetType { get; set; }

    [JsonPropertyName("target_url")]
    public string? TargetUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("project")]
    public GitLabProject? Project { get; set; }

    [JsonPropertyName("target")]
    public GitLabTarget? Target { get; set; }
}

public sealed class GitLabProject
{
    [JsonPropertyName("path_with_namespace")]
    public string? PathWithNamespace { get; set; }
}

public sealed class GitLabTarget
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
