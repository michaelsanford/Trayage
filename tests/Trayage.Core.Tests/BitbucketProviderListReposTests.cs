using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Providers;
using Trayage.Core.Providers.Bitbucket;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

/// <summary>
/// Covers <see cref="BitbucketProvider.ListAccessibleRepositoriesAsync"/>'s "failed vs empty"
/// contract: a non-403 HTTP failure must surface as a <see cref="RepositoryListResult.Partial"/>
/// result (with a warning) rather than a silent empty list that reads as an empty account.
/// </summary>
public sealed class BitbucketProviderListReposTests
{
    private const string TokenUrl = "https://bitbucket.org/site/oauth2/access_token";
    private const string TokenJson =
        "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600,\"scopes\":\"account repository\"}";

    private const string AccountId = "acct1";

    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();
    private readonly ProviderAccountContext _account;

    public BitbucketProviderListReposTests()
    {
        var settings = new TrayageSettings
        {
            Accounts = { new ProviderAccount { Id = AccountId, Provider = ProviderKind.Bitbucket, Connected = true } },
        };
        _settings.Load().Returns(settings);
        _account = new ProviderAccountContext(ProviderKind.Bitbucket, AccountId, _settings, _secrets);

        // A stored refresh token makes the provider report IsConnected on construction.
        _secrets.Contains(_account.RefreshTokenKey).Returns(true);
        _secrets.Get(_account.RefreshTokenKey).Returns("rt");
    }

    private BitbucketProvider NewProvider(Func<HttpRequestMessage, HttpResponseMessage> route)
    {
        var handler = new RoutingHandler(route);
        var client = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);

        var options = Options.Create(new BitbucketOptions { Key = "k", Secret = "s" });
        return new BitbucketProvider(options, _account, factory, _secrets, NullLogger<BitbucketProvider>.Instance);
    }

    [Fact]
    public async Task ListRepos_WorkspacesRequestFails_ReturnsPartialWithWarning_NotSilentEmpty()
    {
        var provider = NewProvider(req =>
        {
            if (IsToken(req))
            {
                return Json(HttpStatusCode.OK, TokenJson);
            }

            // The workspaces call fails with a server error (a non-403 failure).
            if (req.RequestUri!.AbsolutePath.Contains("/user/workspaces"))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await provider.ListAccessibleRepositoriesAsync(CancellationToken.None);

        Assert.True(result.Partial);
        Assert.False(string.IsNullOrWhiteSpace(result.Warning));
        Assert.Empty(result.Repositories);
    }

    [Fact]
    public async Task ListRepos_Succeeds_ReturnsReposNotPartial()
    {
        var provider = NewProvider(req =>
        {
            if (IsToken(req))
            {
                return Json(HttpStatusCode.OK, TokenJson);
            }

            if (req.RequestUri!.AbsolutePath.Contains("/user/workspaces"))
            {
                return Json(HttpStatusCode.OK, "{\"values\":[{\"slug\":\"acme\"}],\"next\":null}");
            }

            if (req.RequestUri.AbsolutePath.Contains("/repositories/"))
            {
                return Json(HttpStatusCode.OK,
                    "{\"values\":[" +
                    "{\"full_name\":\"acme/one\",\"name\":\"one\",\"updated_on\":\"2024-01-01T00:00:00Z\"}," +
                    "{\"full_name\":\"acme/two\",\"name\":\"two\",\"updated_on\":\"2024-02-01T00:00:00Z\"}" +
                    "],\"next\":null}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await provider.ListAccessibleRepositoriesAsync(CancellationToken.None);

        Assert.False(result.Partial);
        Assert.Null(result.Warning);
        Assert.Equal(2, result.Repositories.Count);
        // Most-recently-updated first.
        Assert.Equal("acme/two", result.Repositories[0].FullName);
    }

    private static bool IsToken(HttpRequestMessage req) =>
        req.RequestUri!.AbsoluteUri.StartsWith(TokenUrl, StringComparison.Ordinal);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(route(request));
    }
}
