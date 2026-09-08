namespace Trayage.Core.Providers.GitLab;

/// <summary>
/// GitLab OAuth application configuration. Bound from the "GitLab" section of
/// appsettings.json. Trayage uses the OAuth 2.0 device authorization grant, so the
/// application is a public client: it ships with only an application id (client id) —
/// no client secret and no redirect URI.
/// </summary>
public sealed class GitLabOptions
{
    public const string SectionName = "GitLab";

    /// <summary>The OAuth application id (client id) registered on the GitLab instance.</summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scopes requested during the device flow. <c>api</c> reads the to-do inbox
    /// (<c>/api/v4/todos</c>) and marks to-dos done — GitLab has no narrower write scope, so
    /// marking read costs the full <c>api</c> scope rather than the <c>read_api</c> this used to
    /// request. <c>read_user</c> is the scope that documents the <c>/user</c> endpoint we call
    /// for the account username, so it's requested explicitly rather than relying on <c>api</c>
    /// implicitly covering it.
    ///
    /// An account connected before this change holds a <c>read_api</c> token: reads keep working
    /// and GitLab rejects the write with 403, which surfaces as a prompt to reconnect.
    /// </summary>
    public IList<string> Scopes { get; set; } = new List<string> { "api", "read_user" };

    /// <summary>Base URL of the GitLab instance. Fixed to GitLab.com for this version.</summary>
    public string BaseUrl { get; set; } = "https://gitlab.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApplicationId);
}
