using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Providers.Bitbucket;

/// <summary>
/// A repository the signed-in user can access, for the watched-repo picker.
/// <paramref name="FullName"/> is the "workspace/repo-slug" used as the watched key.
/// </summary>
public sealed record RepositorySummary(string FullName, string Name);

/// <summary>
/// Bitbucket Cloud inbox provider. Bitbucket has no notification inbox and no device
/// flow, so this authenticates with the authorization-code flow over a loopback redirect
/// and assembles an inbox from per-watched-repo pull-request queries:
/// <list type="bullet">
///   <item>open PRs you authored → <see cref="InboxItemKind.Assignment"/>;</item>
///   <item>open PRs where you're a reviewer → <see cref="InboxItemKind.ReviewRequest"/>;</item>
///   <item>all other open PRs → <see cref="InboxItemKind.RepoActivity"/>.</item>
/// </list>
/// Because Bitbucket retired its cross-repo "PRs for a user" endpoint (CHANGE-2770), all
/// three classes are sourced per watched repo — so a PR only surfaces if its repo is watched.
/// Mentions and CI/check status are not surfaced for Bitbucket in this version (no
/// first-class API), which is a known limitation documented in the README.
/// </summary>
public sealed class BitbucketProvider : IInboxProvider
{
    private const string TokenEndpoint = "https://bitbucket.org/site/oauth2/access_token";
    private const string AuthorizeEndpoint = "https://bitbucket.org/site/oauth2/authorize";
    private const string ApiBase = "https://api.bitbucket.org/2.0";
    private const int MaxPagesPerQuery = 5;

    private readonly BitbucketOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretStore _secrets;
    private readonly ISettingsStore _settings;
    private readonly ILogger<BitbucketProvider> _logger;

    private string? _accessToken;
    private DateTimeOffset _accessExpiresUtc = DateTimeOffset.MinValue;
    private string? _userUuid;
    private string? _grantedScopes;

    public BitbucketProvider(
        IOptions<BitbucketOptions> options,
        IHttpClientFactory httpClientFactory,
        ISecretStore secrets,
        ISettingsStore settings,
        ILogger<BitbucketProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
        _settings = settings;
        _logger = logger;

        RestoreSession();
    }

    public ProviderKind Provider => ProviderKind.Bitbucket;

    public bool IsConnected { get; private set; }

    public string? AccountLogin { get; private set; }

    public TimeSpan? SuggestedPollInterval => null;

    /// <summary>
    /// Runs the authorization-code loopback flow. <paramref name="openBrowser"/> is given
    /// the authorize URL to launch; the method then waits for Bitbucket to redirect back
    /// to the loopback listener and exchanges the returned code for tokens.
    /// </summary>
    public async Task ConnectAsync(Func<Uri, Task> openBrowser, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new ProviderNotConfiguredException(
                "No Bitbucket OAuth consumer is configured. Set Bitbucket:Key and Bitbucket:Secret in appsettings.json.");
        }

        var redirect = new Uri(_options.RedirectUri);
        var listenerPrefix = $"{redirect.Scheme}://{redirect.Host}:{redirect.Port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Couldn't listen on {listenerPrefix} for the OAuth redirect. Is another app using that port?", ex);
        }

        try
        {
            var authorizeUrl = new Uri($"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(_options.Key)}&response_type=code");
            await openBrowser(authorizeUrl).ConfigureAwait(false);

            var context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            await WriteBrowserResponseAsync(context.Response, error is null).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"Bitbucket authorization failed: {error}");
            }

            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("Bitbucket did not return an authorization code.");
            }

            var token = await ExchangeCodeAsync(code, cancellationToken).ConfigureAwait(false);
            StoreTokens(token);

            var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
            _userUuid = user?.Uuid;
            AccountLogin = user?.Username ?? user?.DisplayName;
            IsConnected = true;
            PersistConnectionState(connected: true, login: AccountLogin);
            // Note: the account login is PII, so it is not written to the log.
            _logger.LogInformation("Connected to Bitbucket.");
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Disconnect()
    {
        _secrets.Remove(SecretKeys.BitbucketAccessToken);
        _secrets.Remove(SecretKeys.BitbucketRefreshToken);
        _accessToken = null;
        _accessExpiresUtc = DateTimeOffset.MinValue;
        _userUuid = null;
        IsConnected = false;
        AccountLogin = null;
        PersistConnectionState(connected: false, login: null);
        _logger.LogInformation("Disconnected from Bitbucket.");
    }

    public async Task<IReadOnlyList<InboxItem>> FetchInboxAsync(InboxQuery query, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return Array.Empty<InboxItem>();
        }

        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return Array.Empty<InboxItem>();
        }

        var uuid = await EnsureUserUuidAsync(cancellationToken).ConfigureAwait(false);
        if (uuid is null)
        {
            return Array.Empty<InboxItem>();
        }

        // Bitbucket retired the cross-repo "PRs for a user" endpoint (CHANGE-2770), so every
        // class of item — authored, review-requested, and general activity — is now sourced
        // per watched repo. A repo must therefore be watched for its PRs to surface.
        var tasks = new List<Task<List<InboxItem>>>();

        // Limit concurrent HTTP queries using a semaphore. 4 is a safe compromise between speed and rate limits.
        using var semaphore = new SemaphoreSlim(4);
        foreach (var repo in query.WatchedRepositories)
        {
            tasks.Add(GetWatchedRepoAsync(repo, uuid, semaphore, cancellationToken));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Highest-priority kind wins when the same PR appears via multiple queries.
        var byId = new Dictionary<string, InboxItem>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            foreach (var item in result)
            {
                Upsert(byId, item);
            }
        }

        return byId.Values.ToList();
    }

    /// <summary>
    /// Lists the repositories the signed-in user can access, most-recently-updated first.
    /// Bitbucket retired the cross-workspace listing endpoints (CHANGE-2770: both
    /// <c>/2.0/repositories?role=…</c> and <c>/2.0/user/permissions/repositories</c> now 410),
    /// so the supported path is to enumerate the user's workspaces via <c>/2.0/user/workspaces</c>
    /// and list each workspace's repositories with <c>/2.0/repositories/{workspace}</c>. A user
    /// can't hold repo access without workspace membership, so this covers everything they can
    /// reach. Returns an empty list (never throws) when not connected.
    /// </summary>
    public async Task<IReadOnlyList<RepositorySummary>> ListAccessibleRepositoriesAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return Array.Empty<RepositorySummary>();
        }

        var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return Array.Empty<RepositorySummary>();
        }

        try
        {
            var workspaces = await GetUserWorkspaceSlugsAsync(cancellationToken).ConfigureAwait(false);
            if (workspaces.Count == 0)
            {
                return Array.Empty<RepositorySummary>();
            }

            using var semaphore = new SemaphoreSlim(4);
            var tasks = workspaces.Select(ws => GetWorkspaceRepositoriesAsync(ws, semaphore, cancellationToken)).ToList();
            var perWorkspace = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Dedupe by full name (defensive), then most-recently-updated first.
            var byName = new Dictionary<string, BitbucketRepositorySummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var repo in perWorkspace.SelectMany(r => r))
            {
                if (!string.IsNullOrEmpty(repo.FullName))
                {
                    byName[repo.FullName] = repo;
                }
            }

            return byName.Values
                .OrderByDescending(r => BitbucketMapping.ParseTimestamp(r.UpdatedOn))
                .Select(r => new RepositorySummary(r.FullName!, r.Name ?? r.FullName!))
                .ToList();
        }
        catch (InvalidOperationException)
        {
            // A clear, user-actionable problem (e.g. a missing OAuth scope) — surface it to
            // the picker rather than swallowing it into a misleading empty list.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to list accessible Bitbucket repositories.");
            return Array.Empty<RepositorySummary>();
        }
    }

    private async Task<List<string>> GetUserWorkspaceSlugsAsync(CancellationToken ct)
    {
        var slugs = new List<string>();
        var next = $"{ApiBase}/user/workspaces?pagelen=100";
        var page = 0;

        while (next is not null && page < MaxPagesPerQuery)
        {
            var data = await GetWithScopeCheckAsync<BitbucketPagedUserWorkspaces>(next, "account", "Account", ct).ConfigureAwait(false);
            if (data is null)
            {
                break;
            }

            foreach (var workspace in data.Values)
            {
                if (workspace.EffectiveSlug is { Length: > 0 } slug)
                {
                    slugs.Add(slug);
                }
            }

            next = data.Next;
            page++;
        }

        return slugs;
    }

    private async Task<List<BitbucketRepositorySummary>> GetWorkspaceRepositoriesAsync(string workspace, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<BitbucketRepositorySummary>();
            var next = $"{ApiBase}/repositories/{Uri.EscapeDataString(workspace)}?sort=-updated_on&pagelen=100" +
                       "&fields=next,values.full_name,values.name,values.updated_on";
            var page = 0;

            while (next is not null && page < MaxPagesPerQuery)
            {
                var data = await GetWithScopeCheckAsync<BitbucketPagedRepositories>(next, "repository", "Repositories", ct).ConfigureAwait(false);
                if (data is null)
                {
                    break;
                }

                list.AddRange(data.Values);
                next = data.Next;
                page++;
            }

            if (next is not null)
            {
                _logger.LogInformation("Bitbucket workspace {Workspace} has more repositories than the {Max}-page cap; some omitted. Use manual entry to add them.", workspace, MaxPagesPerQuery);
            }

            return list;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// GET that deserializes <typeparamref name="T"/>, but turns a 403 into an actionable
    /// error naming the token's actual scopes and the consumer permission to enable. A token
    /// refresh keeps whatever scopes it was first authorized with, so a fresh reconnect is
    /// required after changing consumer permissions.
    /// </summary>
    private async Task<T?> GetWithScopeCheckAsync<T>(string url, string scope, string consumerPermission, CancellationToken ct) where T : class
    {
        using var response = await SendWithAuthAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var scopes = string.IsNullOrEmpty(_grantedScopes) ? "unknown" : _grantedScopes;
            throw new InvalidOperationException(
                $"Bitbucket returned 403 — this token is missing the \"{scope}\" scope. Its scopes are: {scopes}. " +
                $"Enable \"{consumerPermission}: Read\" on the OAuth consumer (it's separate from Pull requests), then " +
                "Disconnect and reconnect Bitbucket — a token refresh keeps the old scopes; only a fresh authorization picks up new ones.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task<List<InboxItem>> GetWatchedRepoAsync(string repo, string uuid, SemaphoreSlim semaphore, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var list = new List<InboxItem>();

            // Authored by me and review-requested of me are the specific classes; the Upsert
            // priority means the most specific kind wins if a PR matches more than one query.
            var authorUrl = $"{ApiBase}/repositories/{repo}/pullrequests?state=OPEN&q={Uri.EscapeDataString($"author.uuid=\"{uuid}\"")}";
            foreach (var pr in await GetAllPullRequestsAsync(authorUrl, ct).ConfigureAwait(false))
            {
                list.Add(BitbucketMapping.ToInboxItem(pr, InboxItemKind.Assignment, repo));
            }

            var reviewerUrl = $"{ApiBase}/repositories/{repo}/pullrequests?state=OPEN&q={Uri.EscapeDataString($"reviewers.uuid=\"{uuid}\"")}";
            foreach (var pr in await GetAllPullRequestsAsync(reviewerUrl, ct).ConfigureAwait(false))
            {
                list.Add(BitbucketMapping.ToInboxItem(pr, InboxItemKind.ReviewRequest, repo));
            }

            var allUrl = $"{ApiBase}/repositories/{repo}/pullrequests?state=OPEN";
            foreach (var pr in await GetAllPullRequestsAsync(allUrl, ct).ConfigureAwait(false))
            {
                list.Add(BitbucketMapping.ToInboxItem(pr, InboxItemKind.RepoActivity, repo));
            }
            return list;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static void Upsert(Dictionary<string, InboxItem> byId, InboxItem item)
    {
        if (!byId.TryGetValue(item.Id, out var existing) || Priority(item.Kind) > Priority(existing.Kind))
        {
            byId[item.Id] = item;
        }
    }

    private static int Priority(InboxItemKind kind) => kind switch
    {
        InboxItemKind.ReviewRequest => 3,
        InboxItemKind.Assignment => 2,
        _ => 1,
    };

    private async Task<List<BitbucketPullRequest>> GetAllPullRequestsAsync(string url, CancellationToken ct)
    {
        var results = new List<BitbucketPullRequest>();
        var next = url;
        var page = 0;

        try
        {
            while (next is not null && page < MaxPagesPerQuery)
            {
                var pageData = await GetApiAsync<BitbucketPagedPullRequests>(next, ct).ConfigureAwait(false);
                if (pageData is null)
                {
                    break;
                }

                results.AddRange(pageData.Values);
                next = pageData.Next;
                page++;
            }

            if (next is not null)
            {
                _logger.LogInformation("Bitbucket query {Url} has more pages than the {Max}-page cap; some items omitted.", Redact(url), MaxPagesPerQuery);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bitbucket query failed: {Url}", Redact(url));
        }

        return results;
    }

    /// <summary>
    /// Strips a query string (which can carry <c>reviewers.uuid="…"</c>) and masks the
    /// signed-in user's UUID in the path so log lines never leak that PII.
    /// </summary>
    private string Redact(string url)
    {
        var query = url.IndexOf('?');
        var path = query >= 0 ? url[..query] : url;

        if (_userUuid is { Length: > 0 })
        {
            path = path
                .Replace(Uri.EscapeDataString(_userUuid), "{uuid}", StringComparison.Ordinal)
                .Replace(_userUuid, "{uuid}", StringComparison.Ordinal);
        }

        return query >= 0 ? path + "?…" : path;
    }

    private async Task<T?> GetApiAsync<T>(string url, CancellationToken ct) where T : class
    {
        // SendWithAuthAsync hands ownership to the caller, so we own disposal here.
        // Disposing the content stream alone would leak the HttpResponseMessage.
        using var response = await SendWithAuthAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>Sends an authenticated request, refreshing the access token once on 401.</summary>
    private async Task<HttpResponseMessage?> SendWithAuthAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient();
        var request = requestFactory();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // Token may have expired between checks; force a refresh and retry once.
        response.Dispose();
        _accessExpiresUtc = DateTimeOffset.MinValue;
        token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (token is null)
        {
            return null;
        }

        var retry = requestFactory();
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(retry, ct).ConfigureAwait(false);
    }

    private async Task<string?> EnsureUserUuidAsync(CancellationToken ct)
    {
        if (_userUuid is not null)
        {
            return _userUuid;
        }

        var user = await GetCurrentUserAsync(ct).ConfigureAwait(false);
        _userUuid = user?.Uuid;
        return _userUuid;
    }

    private async Task<BitbucketUser?> GetCurrentUserAsync(CancellationToken ct) =>
        await GetApiAsync<BitbucketUser>($"{ApiBase}/user", ct).ConfigureAwait(false);

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && _accessExpiresUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _accessToken;
        }

        var refreshToken = _secrets.Get(SecretKeys.BitbucketRefreshToken);
        if (refreshToken is null)
        {
            return null;
        }

        try
        {
            var token = await RefreshAsync(refreshToken, ct).ConfigureAwait(false);
            StoreTokens(token);
            return _accessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to refresh the Bitbucket access token.");
            return null;
        }
    }

    private async Task<BitbucketTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
        };
        return await PostTokenAsync(form, ct).ConfigureAwait(false);
    }

    private async Task<BitbucketTokenResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        return await PostTokenAsync(form, ct).ConfigureAwait(false);
    }

    private async Task<BitbucketTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Key}:{_options.Secret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var token = await JsonSerializer.DeserializeAsync<BitbucketTokenResponse>(stream, cancellationToken: ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Bitbucket returned an empty token response.");

        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException("Bitbucket returned an empty access token.");
        }

        return token;
    }

    private void StoreTokens(BitbucketTokenResponse token)
    {
        _accessToken = token.AccessToken;
        _accessExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60));
        _grantedScopes = token.Scopes;

        // The scopes are not secret and not PII, but they're the single best clue when an
        // API call 403s: a refresh keeps the scopes the token was first authorized with, so
        // this reveals whether a fresh re-authorization is needed to pick up consumer changes.
        _logger.LogInformation("Bitbucket token scopes: {Scopes}", string.IsNullOrEmpty(token.Scopes) ? "(none reported)" : token.Scopes);

        if (!string.IsNullOrEmpty(token.AccessToken))
        {
            _secrets.Set(SecretKeys.BitbucketAccessToken, token.AccessToken);
        }

        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            _secrets.Set(SecretKeys.BitbucketRefreshToken, token.RefreshToken);
        }
    }

    private void RestoreSession()
    {
        if (_secrets.Contains(SecretKeys.BitbucketRefreshToken))
        {
            IsConnected = true;
            AccountLogin = _settings.Load().Bitbucket.AccountLogin;
        }
    }

    private void PersistConnectionState(bool connected, string? login)
    {
        var settings = _settings.Load();
        settings.Bitbucket.Connected = connected;
        settings.Bitbucket.AccountLogin = login;
        _settings.Save(settings);
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool success)
    {
        var message = success
            ? "Trayage is now connected to Bitbucket. You can close this tab."
            : "Bitbucket authorization failed. You can close this tab and try again.";
        var html = $"<!doctype html><html><body style=\"font-family:Segoe UI,sans-serif;padding:2rem\"><h2>{message}</h2></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }
}
