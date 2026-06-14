namespace Trayage.Core.Providers.GitHub;

/// <summary>
/// GitHub OAuth App configuration. Bound from the "GitHub" section of appsettings.json.
/// The client id is registered once by the app author and shipped with the app; device
/// flow needs no client secret.
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth scopes requested during device flow. <c>notifications</c> reads the inbox;
    /// <c>repo</c> is needed for notifications originating from private repositories.
    /// </summary>
    public IList<string> Scopes { get; set; } = new List<string> { "notifications", "repo", "read:org" };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
