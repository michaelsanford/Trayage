using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;
using Trayage.Core.Providers.Bitbucket;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

/// <summary>
/// Covers <see cref="BitbucketProvider.FetchInboxAsync"/>: one pull-request query per watched
/// repo, classified locally, and transient failures surfacing as exceptions (so the inbox keeps
/// the account's last-known items) rather than as a short list.
/// </summary>
public sealed class BitbucketProviderFetchInboxTests
{
    private const string Me = "{11111111-1111-1111-1111-111111111111}";
    private const string Someone = "{22222222-2222-2222-2222-222222222222}";

    private const string TokenJson =
        "{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600,\"scopes\":\"account repository pullrequest\"}";

    private readonly InMemorySecretStore _secrets = new();
    private readonly ProviderAccountContext _account;
    private readonly List<string> _pullRequestUrls = [];

    public BitbucketProviderFetchInboxTests()
    {
        var settings = Substitute.For<ISettingsStore>();
        settings.Load().Returns(new TrayageSettings
        {
            Accounts = { new ProviderAccount { Id = "acct1", Provider = ProviderKind.Bitbucket, Connected = true } },
        });
        _account = new ProviderAccountContext(ProviderKind.Bitbucket, "acct1", settings, _secrets);
        _secrets.Set(_account.RefreshTokenKey, "rt");
    }

    private BitbucketProvider NewProvider(Func<string, HttpResponseMessage> pullRequests)
    {
        var factory = TestHttp.Factory(req =>
        {
            var uri = req.RequestUri!;
            if (uri.Host == "bitbucket.org")
            {
                return TestHttp.Json(HttpStatusCode.OK, TokenJson);
            }

            if (uri.AbsolutePath == "/2.0/user")
            {
                return TestHttp.Json(HttpStatusCode.OK, $"{{\"uuid\":\"{Me}\",\"username\":\"me\"}}");
            }

            lock (_pullRequestUrls)
            {
                _pullRequestUrls.Add(uri.AbsoluteUri);
            }

            var repo = uri.AbsolutePath["/2.0/repositories/".Length..^"/pullrequests".Length];
            return pullRequests(repo);
        });

        return new BitbucketProvider(Options.Create(new BitbucketOptions { Key = "k", Secret = "s" }), _account, factory,
            _secrets, NullLogger<BitbucketProvider>.Instance);
    }

    private static string Pr(int id, string author, params string[] reviewers) =>
        $"{{\"id\":{id},\"title\":\"PR {id}\",\"updated_on\":\"2026-01-0{id}T00:00:00Z\"," +
        $"\"author\":{{\"uuid\":\"{author}\"}}," +
        $"\"reviewers\":[{string.Join(',', reviewers.Select(r => $"{{\"uuid\":\"{r}\"}}"))}]}}";

    private static HttpResponseMessage Page(params string[] prs) =>
        TestHttp.Json(HttpStatusCode.OK, $"{{\"values\":[{string.Join(',', prs)}],\"next\":null}}");

    [Fact]
    public async Task OneQueryPerRepo_ClassifiesAgainstSignedInUser()
    {
        var provider = NewProvider(_ => Page(
            Pr(1, Me),
            Pr(2, Someone, Me),
            Pr(3, Someone, Someone)));

        var items = await provider.FetchInboxAsync(new InboxQuery(["acme/widgets"]), CancellationToken.None);

        var kinds = items.OrderBy(i => i.Id).Select(i => i.Kind).ToList();
        Assert.Equal([InboxItemKind.Assignment, InboxItemKind.ReviewRequest, InboxItemKind.RepoActivity], kinds);

        var url = Assert.Single(_pullRequestUrls);
        Assert.Contains("state=OPEN", url);
        Assert.Contains("pagelen=50", url);
        Assert.Contains("fields=%2Bvalues.reviewers", url);
    }

    [Fact]
    public async Task TransientRepoFailure_Throws_SoLastKnownItemsAreKept()
    {
        var provider = NewProvider(repo => repo == "acme/broken"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Page(Pr(1, Me)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.FetchInboxAsync(new InboxQuery(["acme/widgets", "acme/broken"]), CancellationToken.None));
    }

    [Fact]
    public async Task MissingRepo_IsSkipped_NotAFailure()
    {
        var provider = NewProvider(repo => repo == "acme/deleted"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Page(Pr(1, Me)));

        var items = await provider.FetchInboxAsync(new InboxQuery(["acme/widgets", "acme/deleted"]), CancellationToken.None);

        Assert.Single(items);
    }

    [Fact]
    public async Task FailedTokenRefresh_Throws()
    {
        var factory = TestHttp.Factory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var provider = new BitbucketProvider(Options.Create(new BitbucketOptions { Key = "k", Secret = "s" }), _account, factory,
            _secrets, NullLogger<BitbucketProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.FetchInboxAsync(new InboxQuery(["acme/widgets"]), CancellationToken.None));
    }
}
