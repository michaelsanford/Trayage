namespace Trayage.Core.Security;

/// <summary>Well-known keys used with <see cref="ISecretStore"/>.</summary>
public static class SecretKeys
{
    public const string GitHubAccessToken = "github.access_token";

    public const string BitbucketAccessToken = "bitbucket.access_token";
    public const string BitbucketRefreshToken = "bitbucket.refresh_token";

    public const string GitLabAccessToken = "gitlab.access_token";
    public const string GitLabRefreshToken = "gitlab.refresh_token";
}
