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
    /// OAuth scopes requested during the device flow. <c>read_api</c> reads the to-do inbox
    /// (<c>/api/v4/todos</c>); <c>read_user</c> is the scope that documents the <c>/user</c>
    /// endpoint we call for the account username, so it's requested explicitly rather than
    /// relying on <c>read_api</c> implicitly covering it. A write scope (<c>api</c>) would
    /// only be needed to mark to-dos done, which this version does not do.
    /// </summary>
    public IList<string> Scopes { get; set; } = new List<string> { "read_api", "read_user" };

    /// <summary>Base URL of the GitLab instance. Fixed to GitLab.com for this version.</summary>
    public string BaseUrl { get; set; } = "https://gitlab.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApplicationId);
}
