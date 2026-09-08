using Trayage.Core.Models;

namespace Trayage.Core.Security;

/// <summary>
/// Derives the <see cref="ISecretStore"/> keys under which an account's tokens are stored.
/// Keys are scoped by account id, so several accounts on the same provider coexist without
/// overwriting one another.
/// </summary>
public static class SecretKeys
{
    public static string AccessToken(ProviderKind provider, string accountId) =>
        $"{Slug(provider)}.{Require(accountId)}.access_token";

    public static string RefreshToken(ProviderKind provider, string accountId) =>
        $"{Slug(provider)}.{Require(accountId)}.refresh_token";

    /// <summary>Every key an account can own — used to purge its secrets when it is removed.</summary>
    public static IEnumerable<string> AllFor(ProviderKind provider, string accountId)
    {
        yield return AccessToken(provider, accountId);
        yield return RefreshToken(provider, accountId);
    }

    private static string Slug(ProviderKind provider) => provider switch
    {
        ProviderKind.GitHub => "github",
        ProviderKind.Bitbucket => "bitbucket",
        ProviderKind.GitLab => "gitlab",
        _ => provider.ToString().ToLowerInvariant(),
    };

    private static string Require(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return accountId;
    }

    /// <summary>
    /// The flat, single-account-per-provider keys used before accounts existed. Read once by
    /// <see cref="Trayage.Core.Configuration.SettingsMigration"/>, which re-keys them and
    /// deletes them. Nothing else should reference these.
    /// </summary>
    public static class Legacy
    {
        public const string GitHubAccessToken = "github.access_token";

        public const string BitbucketAccessToken = "bitbucket.access_token";
        public const string BitbucketRefreshToken = "bitbucket.refresh_token";

        public const string GitLabAccessToken = "gitlab.access_token";
        public const string GitLabRefreshToken = "gitlab.refresh_token";

        /// <summary>The legacy (access, refresh) key pair for a provider; refresh is null where unused.</summary>
        public static (string Access, string? Refresh) For(ProviderKind provider) => provider switch
        {
            ProviderKind.GitHub => (GitHubAccessToken, null),
            ProviderKind.Bitbucket => (BitbucketAccessToken, BitbucketRefreshToken),
            ProviderKind.GitLab => (GitLabAccessToken, GitLabRefreshToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }
}
