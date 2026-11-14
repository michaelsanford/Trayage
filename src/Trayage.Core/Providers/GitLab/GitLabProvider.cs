using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Providers.GitLab;

/// <summary>
/// GitLab inbox provider for a single account. GitLab has a centralized, server-side to-do inbox
/// (<c>GET /api/v4/todos</c>), so — like GitHub and unlike Bitbucket — there is no in-app
/// repository filtering: each pending to-do becomes an <see cref="InboxItem"/>.
///
/// Authentication uses the OAuth 2.0 device authorization grant (GitHub-style: the user
/// enters a code in a browser; no client secret, no loopback redirect). GitLab access
/// tokens expire, so the access/refresh tokens are persisted and refreshed on demand,
/// Bitbucket-style.
/// </summary>
public sealed class GitLabProvider : IInboxProvider
{
    private const int MaxPagesPerQuery = 5;

    private readonly GitLabOptions _options;
    private readonly ProviderAccountContext _account;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitLabProvider> _logger;
    private readonly OAuthTokenSession _session;

    public GitLabProvider(
        IOptions<GitLabOptions> options,
        ProviderAccountContext account,
        IHttpClientFactory httpClientFactory,
        ISecretStore secrets,
        ILogger<GitLabProvider> logger)
    {
        _options = options.Value;
        _account = account;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _session = new OAuthTokenSession(account, secrets, httpClientFactory, RefreshAsync, logger);

        if (_session.HasStoredSession)
        {
            IsConnected = true;
            AccountLogin = _account.AccountLogin;
        }
    }

    public ProviderKind Provider => ProviderKind.GitLab;

    public string AccountId => _account.AccountId;

    public string DisplayLabel => _account.QualifiedLabel;

    public bool IsConnected { get; private set; }

    public string? AccountLogin { get; private set; }

    public TimeSpan? SuggestedPollInterval => null;

    /// <summary>
    /// The account's own instance when set, else the app-wide default (gitlab.com). Reads the
    /// settings file, so hot paths take it once per operation rather than per request.
    /// </summary>
    private string InstanceUrl => NormalizeInstanceUrl(_account.BaseUrl is { Length: > 0 } url ? url : _options.BaseUrl);

    /// <summary>
    /// Forces the instance URL onto https, whatever scheme (if any) was configured: the device
    /// code, OAuth tokens and bearer-authenticated API calls all go to this host, so they must
    /// never travel in cleartext. <c>http://gitlab.example.com:8080/</c> becomes
    /// <c>https://gitlab.example.com:8080</c>; a bare host gains the scheme. Any query or
    /// fragment is dropped so it can't corrupt the endpoint paths built on top of it.
    /// </summary>
    internal static string NormalizeInstanceUrl(string raw)
    {
        var trimmed = raw.Trim();
        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        var rest = schemeEnd >= 0 ? trimmed[(schemeEnd + 3)..] : trimmed;

        if (rest.Length == 0
            || !Uri.TryCreate($"https://{rest}", UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new ProviderNotConfiguredException(
                $"The GitLab instance URL '{raw}' isn't a valid address.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private string DeviceAuthEndpoint => $"{InstanceUrl}/oauth/authorize_device";

    private string TokenEndpoint => $"{InstanceUrl}/oauth/token";

    private string ApiBase => $"{InstanceUrl}/api/v4";

    /// <summary>
    /// Runs the OAuth device flow. <paramref name="onPromptReady"/> is invoked with the user
    /// code and verification URL (show it and open the browser); the returned task then polls
    /// GitLab until the user authorises, the code expires, or authorization is denied.
    /// </summary>
    public async Task ConnectAsync(Func<DeviceCodePrompt, Task> onPromptReady, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new ProviderNotConfiguredException(
                "No GitLab OAuth application id is configured. Set GitLab:ApplicationId in appsettings.json.");
        }

        var device = await RequestDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(device.DeviceCode) || string.IsNullOrEmpty(device.UserCode) ||
            string.IsNullOrEmpty(device.VerificationUri))
        {
            throw new InvalidOperationException("GitLab returned an incomplete device authorization response.");
        }

        await onPromptReady(new DeviceCodePrompt(
            device.UserCode,
            device.VerificationUri,
            TimeSpan.FromSeconds(device.ExpiresIn))).ConfigureAwait(false);

        var token = await PollForTokenAsync(device, cancellationToken).ConfigureAwait(false);
        _session.Store(new OAuthTokens(token.AccessToken!, token.RefreshToken, token.ExpiresIn));

        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        AccountLogin = user?.Username;
        IsConnected = true;
        _account.PersistConnection(connected: true, login: AccountLogin);
        // Note: the account login is PII, so it is not written to the log.
        _logger.LogInformation("Connected to GitLab.");
    }

    public void Disconnect()
    {
        _account.PurgeSecrets();
        _session.Clear();
        IsConnected = false;
        AccountLogin = null;
        _account.PersistConnection(connected: false, login: null);
        _logger.LogInformation("Disconnected from GitLab.");
    }

    public async Task<IReadOnlyList<InboxItem>> FetchInboxAsync(InboxQuery query, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return Array.Empty<InboxItem>();
        }

        var apiBase = ApiBase;
        var items = new List<InboxItem>();
        var page = 1;

        while (page <= MaxPagesPerQuery)
        {
            // state defaults to "pending" — the actionable to-do inbox.
            var url = $"{apiBase}/todos?per_page=100&page={page}";
            using var response = await _session.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Couldn't authenticate with GitLab; the session may need reconnecting.");

            // Throw rather than return what we have: a short or empty list would read as "these
            // to-dos are gone", and they'd all notify again once GitLab recovers. A failure lets
            // the inbox keep this account's last-known items and report the outage instead.
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var todos = await JsonSerializer.DeserializeAsync<List<GitLabTodo>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (todos is null || todos.Count == 0)
            {
                break;
            }

            foreach (var todo in todos)
            {
                items.Add(GitLabMapping.ToInboxItem(todo, AccountId));
            }

            // GitLab paginates via the X-Next-Page header (empty when there are no more pages).
            var nextPage = response.Headers.TryGetValues("X-Next-Page", out var values) ? values.FirstOrDefault() : null;
            if (string.IsNullOrEmpty(nextPage) || !int.TryParse(nextPage, out var parsed))
            {
                break;
            }

            page = parsed;
        }

        _logger.LogDebug("Fetched {Count} GitLab to-do(s).", items.Count);
        return items;
    }

    private async Task<GitLabDeviceAuthResponse> RequestDeviceCodeAsync(CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _options.ApplicationId,
            ["scope"] = string.Join(' ', _options.Scopes),
        };

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, DeviceAuthEndpoint);
        request.Content = new FormUrlEncodedContent(form);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<GitLabDeviceAuthResponse>(stream, cancellationToken: ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("GitLab returned an empty device authorization response.");
    }

    private async Task<GitLabTokenResponse> PollForTokenAsync(GitLabDeviceAuthResponse device, CancellationToken ct)
    {
        // GitLab's poll interval (seconds); the server can ask us to back off via slow_down.
        var interval = TimeSpan.FromSeconds(Math.Max(device.Interval, 5));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(device.ExpiresIn > 0 ? device.ExpiresIn : 300);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(interval, ct).ConfigureAwait(false);

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = device.DeviceCode!,
                ["client_id"] = _options.ApplicationId,
            };

            var (status, token) = await PostTokenFormAsync(form, ct).ConfigureAwait(false);

            if (status == HttpStatusCode.OK && !string.IsNullOrEmpty(token?.AccessToken))
            {
                return token;
            }

            switch (token?.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "access_denied":
                    throw new InvalidOperationException("GitLab authorization was denied.");
                case "expired_token":
                    throw new InvalidOperationException("The GitLab device code expired before authorization. Please try again.");
                default:
                    throw new InvalidOperationException(
                        $"GitLab authorization failed: {token?.ErrorDescription ?? token?.Error ?? status.ToString()}");
            }
        }

        throw new InvalidOperationException("The GitLab device code expired before authorization. Please try again.");
    }

    /// <summary>Posts to the token endpoint and returns the status + parsed body (which carries
    /// the device-flow poll <c>error</c> on a non-success status).</summary>
    private async Task<(HttpStatusCode Status, GitLabTokenResponse? Token)> PostTokenFormAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        request.Content = new FormUrlEncodedContent(form);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        GitLabTokenResponse? token = null;
        try
        {
            token = await JsonSerializer.DeserializeAsync<GitLabTokenResponse>(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Non-JSON error body; leave token null and let the caller surface the status.
        }

        return (response.StatusCode, token);
    }

    /// <summary>Redeems a refresh token; <c>null</c> when GitLab rejects it.</summary>
    private async Task<OAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ApplicationId,
        };

        var (status, token) = await PostTokenFormAsync(form, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK || string.IsNullOrEmpty(token?.AccessToken))
        {
            _logger.LogWarning("Failed to refresh the GitLab access token ({Status}).", status);
            return null;
        }

        return new OAuthTokens(token.AccessToken, token.RefreshToken, token.ExpiresIn);
    }

    /// <summary>
    /// Marks the to-do done. Note this <em>removes</em> the item from the inbox rather than
    /// dimming it: Trayage reads the pending to-do list, and a done to-do is no longer pending,
    /// so the row disappears on the next poll. That's GitLab's model, not a choice here.
    /// </summary>
    public async Task<MarkAsReadOutcome> TryMarkAsReadAsync(InboxItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!IsConnected || !TryParseTodoId(item.Id, out var todoId))
        {
            return MarkAsReadOutcome.NotSupported;
        }

        var url = $"{ApiBase}/todos/{todoId}/mark_as_done";
        using var response = await _session.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, url), cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return MarkAsReadOutcome.NotSupported;
        }

        // An account connected before Trayage asked for the write scope holds a read-only token,
        // which GitLab rejects with 403. Surfacing that is the caller's job; the local mark still
        // applies, so the feature degrades rather than breaking.
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogInformation("GitLab refused a to-do write; the account's token lacks the api scope.");
            return MarkAsReadOutcome.NeedsReauthorization;
        }

        // 404 means the to-do is already done or gone — the desired state either way.
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return MarkAsReadOutcome.Propagated;
        }

        _logger.LogWarning("GitLab returned {Status} marking a to-do done.", (int)response.StatusCode);
        return MarkAsReadOutcome.NotSupported;
    }

    /// <summary>Recovers the numeric to-do id from the "todo:123" form GitLabMapping builds.</summary>
    private static bool TryParseTodoId(string id, out long todoId)
    {
        const string prefix = "todo:";
        todoId = 0;
        return id.StartsWith(prefix, StringComparison.Ordinal) &&
               long.TryParse(id.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out todoId);
    }

    private async Task<GitLabUser?> GetCurrentUserAsync(CancellationToken ct)
    {
        var url = $"{ApiBase}/user";
        using var response = await _session.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<GitLabUser>(stream, cancellationToken: ct).ConfigureAwait(false);
    }
}
