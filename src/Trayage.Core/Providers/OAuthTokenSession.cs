using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Trayage.Core.Security;

namespace Trayage.Core.Providers;

/// <summary>A token-endpoint response, normalised across providers.</summary>
internal sealed record OAuthTokens(string AccessToken, string? RefreshToken, int ExpiresInSeconds);

/// <summary>
/// The access/refresh-token half of an OAuth provider that issues expiring access tokens
/// (GitLab, Bitbucket). Owns the in-memory access token, persists both tokens to the
/// account's secret slots, refreshes on demand, and sends bearer-authenticated requests
/// with a single retry on 401.
/// </summary>
/// <remarks>
/// Refreshes are single-flight. A poll, a mark-as-read burst and Bitbucket's per-repo fan-out
/// can all find the token expired at the same moment; letting each POST its own refresh with
/// the same refresh token breaks providers that rotate refresh tokens (GitLab): every request
/// but the first gets <c>invalid_grant</c>, and the last one to finish may persist a token
/// that has already been rotated out.
/// </remarks>
internal sealed class OAuthTokenSession(
    ProviderAccountContext account,
    ISecretStore secrets,
    IHttpClientFactory httpClientFactory,
    Func<string, CancellationToken, Task<OAuthTokens?>> refresh,
    ILogger logger)
{
    /// <summary>Refresh slightly early so a token can't expire in flight.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // Token and expiry swap together as one immutable value, so a reader on another thread
    // never pairs a new token with an old expiry.
    private volatile AccessGrant? _grant;

    /// <summary>Whether a refresh token is stored, i.e. the account has a resumable session.</summary>
    public bool HasStoredSession => secrets.Contains(account.RefreshTokenKey);

    /// <summary>Adopts freshly issued tokens and persists them to the account's secret slots.</summary>
    public void Store(OAuthTokens tokens)
    {
        _grant = new AccessGrant(tokens.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(tokens.ExpiresInSeconds, 60)));

        secrets.Set(account.AccessTokenKey, tokens.AccessToken);
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            secrets.Set(account.RefreshTokenKey, tokens.RefreshToken);
        }
    }

    /// <summary>Forgets the in-memory token. Stored secrets are purged by the account, not here.</summary>
    public void Clear() => _grant = null;

    /// <summary>A usable access token, refreshing first if needed; <c>null</c> when that fails.</summary>
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        GetAccessTokenAsync(rejected: null, cancellationToken);

    /// <summary>
    /// Sends a bearer-authenticated request built by <paramref name="requestFactory"/> (called
    /// again for the retry, since a request can't be sent twice). On 401 the token is refreshed
    /// once and the request retried. Returns <c>null</c> when no usable token can be obtained;
    /// otherwise the caller owns, and must dispose, the response.
    /// </summary>
    public async Task<HttpResponseMessage?> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        var client = httpClientFactory.CreateClient();
        var response = await SendWithTokenAsync(client, requestFactory, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // The token was revoked or expired early. Refresh unless another request already
        // replaced this exact token, then retry once.
        response.Dispose();
        token = await GetAccessTokenAsync(rejected: token, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        return await SendWithTokenAsync(client, requestFactory, token, cancellationToken).ConfigureAwait(false);
    }

    private static Task<HttpResponseMessage> SendWithTokenAsync(
        HttpClient client, Func<HttpRequestMessage> requestFactory, string token, CancellationToken cancellationToken)
    {
        var request = requestFactory();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request, cancellationToken);
    }

    /// <param name="rejected">A token the server just refused; never handed out again.</param>
    private async Task<string?> GetAccessTokenAsync(string? rejected, CancellationToken cancellationToken)
    {
        if (Usable(rejected) is { } current)
        {
            return current;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Whoever held the gate before us may already have refreshed.
            if (Usable(rejected) is { } refreshed)
            {
                return refreshed;
            }

            var refreshToken = secrets.Get(account.RefreshTokenKey);
            if (refreshToken is null)
            {
                return null;
            }

            var tokens = await refresh(refreshToken, cancellationToken).ConfigureAwait(false);
            if (tokens is null)
            {
                return null;
            }

            Store(tokens);
            return tokens.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to refresh the access token.");
            return null;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private string? Usable(string? rejected)
    {
        var grant = _grant;
        return grant is not null && grant.Token != rejected && grant.ExpiresUtc > DateTimeOffset.UtcNow + ExpirySkew
            ? grant.Token
            : null;
    }

    private sealed record AccessGrant(string Token, DateTimeOffset ExpiresUtc);
}
