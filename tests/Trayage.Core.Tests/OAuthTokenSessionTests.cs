using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Providers;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

public sealed class OAuthTokenSessionTests
{
    private const string ApiUrl = "https://api.example.test/thing";

    private readonly InMemorySecretStore _secrets = new();
    private readonly ProviderAccountContext _account;
    private int _refreshCalls;

    public OAuthTokenSessionTests()
    {
        _account = new ProviderAccountContext(ProviderKind.GitLab, "acct1", Substitute.For<ISettingsStore>(), _secrets);
        _secrets.Set(_account.RefreshTokenKey, "rt-0");
    }

    /// <summary>Each refresh issues "at-N"/"rt-N" and rotates the refresh token, like GitLab.</summary>
    private OAuthTokenSession NewSession(Func<HttpRequestMessage, HttpResponseMessage> route, TimeSpan? refreshDelay = null) =>
        new(_account, _secrets, TestHttp.Factory(route), async (refreshToken, ct) =>
        {
            var n = Interlocked.Increment(ref _refreshCalls);
            if (refreshDelay is { } delay)
            {
                await Task.Delay(delay, ct);
            }

            // A rotated-out refresh token is rejected, as GitLab does.
            return refreshToken == $"rt-{n - 1}" ? new OAuthTokens($"at-{n}", $"rt-{n}", 3600) : null;
        }, NullLogger.Instance);

    private static HttpRequestMessage Get() => new(HttpMethod.Get, ApiUrl);

    [Fact]
    public async Task ConcurrentRequests_WithNoToken_RefreshOnce()
    {
        var session = NewSession(_ => new HttpResponseMessage(HttpStatusCode.OK), refreshDelay: TimeSpan.FromMilliseconds(50));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => session.SendAsync(Get, CancellationToken.None)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r!.StatusCode));
        Assert.Equal(1, _refreshCalls);
        Assert.Equal("rt-1", _secrets.Get(_account.RefreshTokenKey));
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_AndRetriesWithNewToken()
    {
        var seen = new List<string>();
        var session = NewSession(req =>
        {
            var token = req.Headers.Authorization!.Parameter!;
            lock (seen)
            {
                seen.Add(token);
            }

            return new HttpResponseMessage(token == "stale" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        });
        session.Store(new OAuthTokens("stale", RefreshToken: null, 3600));

        using var response = await session.SendAsync(Get, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(["stale", "at-1"], seen);
        Assert.Equal(1, _refreshCalls);
    }

    [Fact]
    public async Task ConcurrentUnauthorized_OnSameToken_RefreshOnce()
    {
        var session = NewSession(req =>
            new HttpResponseMessage(req.Headers.Authorization!.Parameter == "stale" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK),
            refreshDelay: TimeSpan.FromMilliseconds(50));
        session.Store(new OAuthTokens("stale", RefreshToken: null, 3600));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => session.SendAsync(Get, CancellationToken.None)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r!.StatusCode));
        Assert.Equal(1, _refreshCalls);
    }

    [Fact]
    public async Task FailedRefresh_ReturnsNull()
    {
        _secrets.Set(_account.RefreshTokenKey, "revoked");
        var session = NewSession(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.Null(await session.SendAsync(Get, CancellationToken.None));
    }

    [Fact]
    public async Task NoStoredRefreshToken_ReturnsNull_WithoutRefreshing()
    {
        _secrets.Remove(_account.RefreshTokenKey);
        var session = NewSession(_ => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.False(session.HasStoredSession);
        Assert.Null(await session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(0, _refreshCalls);
    }

    [Fact]
    public void Store_PersistsBothTokens_AndKeepsRefreshTokenWhenNoneIssued()
    {
        var session = NewSession(_ => new HttpResponseMessage(HttpStatusCode.OK));

        session.Store(new OAuthTokens("at-x", "rt-x", 3600));
        Assert.Equal("at-x", _secrets.Get(_account.AccessTokenKey));
        Assert.Equal("rt-x", _secrets.Get(_account.RefreshTokenKey));

        session.Store(new OAuthTokens("at-y", RefreshToken: null, 3600));
        Assert.Equal("at-y", _secrets.Get(_account.AccessTokenKey));
        Assert.Equal("rt-x", _secrets.Get(_account.RefreshTokenKey));
    }

    [Fact]
    public async Task Clear_ForcesRefreshOnNextUse()
    {
        var session = NewSession(_ => new HttpResponseMessage(HttpStatusCode.OK));
        session.Store(new OAuthTokens("at-0", RefreshToken: null, 3600));

        session.Clear();

        Assert.Equal("at-1", await session.GetAccessTokenAsync(CancellationToken.None));
    }
}
