using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers.Bitbucket;
using Trayage.Core.Providers.GitHub;
using Trayage.Core.Providers.GitLab;
using Trayage.Core.Security;

namespace Trayage.Core.Providers;

/// <summary>Builds the provider instance that serves one account.</summary>
public interface IProviderFactory
{
    IInboxProvider Create(ProviderAccount account);
}

/// <summary>
/// Creates providers per account, handing each the shared, app-wide dependencies (the OAuth
/// client identity from <c>appsettings.json</c>, the HTTP factory, the stores) plus its own
/// <see cref="ProviderAccountContext"/>.
/// </summary>
public sealed class ProviderFactory(
    IOptions<GitHubOptions> gitHubOptions,
    IOptions<BitbucketOptions> bitbucketOptions,
    IOptions<GitLabOptions> gitLabOptions,
    IHttpClientFactory httpClientFactory,
    ISecretStore secrets,
    ISettingsStore settings,
    ILoggerFactory loggerFactory) : IProviderFactory
{
    public IInboxProvider Create(ProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var context = new ProviderAccountContext(account.Provider, account.Id, settings, secrets);

        return account.Provider switch
        {
            ProviderKind.GitHub => new GitHubProvider(
                gitHubOptions, context, secrets, loggerFactory.CreateLogger<GitHubProvider>()),

            ProviderKind.Bitbucket => new BitbucketProvider(
                bitbucketOptions, context, httpClientFactory, secrets, loggerFactory.CreateLogger<BitbucketProvider>()),

            ProviderKind.GitLab => new GitLabProvider(
                gitLabOptions, context, httpClientFactory, secrets, loggerFactory.CreateLogger<GitLabProvider>()),

            _ => throw new ArgumentOutOfRangeException(nameof(account), account.Provider, "Unknown provider."),
        };
    }
}
