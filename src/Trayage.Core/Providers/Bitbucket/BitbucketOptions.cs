namespace Trayage.Core.Providers.Bitbucket;

/// <summary>
/// Bitbucket Cloud OAuth consumer configuration, bound from the "Bitbucket" section of
/// appsettings.json. Because Bitbucket Cloud doesn't support the device flow, Trayage
/// uses the authorization-code flow with a fixed loopback redirect; the consumer's
/// callback URL must therefore be set to <see cref="RedirectUri"/>.
/// </summary>
public sealed class BitbucketOptions
{
    public const string SectionName = "Bitbucket";

    public string Key { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public IList<string> Scopes { get; set; } = new List<string> { "account", "repository", "pullrequest" };

    /// <summary>
    /// Loopback callback registered with the Bitbucket consumer. The port is fixed so it
    /// can match the consumer's configured callback URL.
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost:33418/callback";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Secret);
}
