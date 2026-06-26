using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Providers.GitLab;

/// <summary>
/// GitLab.com inbox provider. GitLab has a centralized, server-side to-do inbox
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISecretStore _secrets;
    private readonly ISettingsStore _settings;
    private readonly ILogger<GitLabProvider> _logger;

    private string? _accessToken;
    private DateTimeOffset _accessExpiresUtc = DateTimeOffset.MinValue;

    public GitLabProvider(
        IOptions<GitLabOptions> options,
        IHttpClientFactory httpClientFactory,
        ISecretStore secrets,
        ISettingsStore settings,
        ILogger<GitLabProvider> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
        _settings = settings;
        _logger = logger;

        RestoreSession();
    }

    public ProviderKind Provider => ProviderKind.GitLab;

    public bool IsConnected { get; private set; }

    public string? AccountLogin { get; private set; }

    public TimeSpan? SuggestedPollInterval => null;

    private string DeviceAuthEndpoint => $"{_options.BaseUrl.TrimEnd('/')}/oauth/authorize_device";

    private string TokenEndpoint => $"{_options.BaseUrl.TrimEnd('/')}/oauth/token";

    private string ApiBase => $"{_options.BaseUrl.TrimEnd('/')}/api/v4";

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
        StoreTokens(token);

        var user = await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        AccountLogin = user?.Username;
        IsConnected = true;
        PersistConnectionState(connected: true, login: AccountLogin);
        // Note: the account login is PII, so it is not written to the log.
        _logger.LogInformation("Connected to GitLab.");
    }

    public void Disconnect()
    {
        _secrets.Remove(SecretKeys.GitLabAccessToken);
        _secrets.Remove(SecretKeys.GitLabRefreshToken);
        _accessToken = null;
        _accessExpiresUtc = DateTimeOffset.MinValue;
        IsConnected = false;
        AccountLogin = null;
        PersistConnectionState(connected: false, login: null);
        _logger.LogInformation("Disconnected from GitLab.");
    }

    public async Task<IReadOnlyList<InboxItem>> FetchInboxAsync(InboxQuery query, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return Array.Empty<InboxItem>();
        }

        var items = new List<InboxItem>();
        var page = 1;

        while (page <= MaxPagesPerQuery)
        {
            // state defaults to "pending" — the actionable to-do inbox.
            var url = $"{ApiBase}/todos?per_page=100&page={page}";
            using var response = await SendWithAuthAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
            if (response is null || !response.IsSuccessStatusCode)
            {
                break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var todos = await JsonSerializer.DeserializeAsync<List<GitLabTodo>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (todos is null || todos.Count == 0)
            {
                break;
            }

            foreach (var todo in todos)
            {
                items.Add(GitLabMapping.ToInboxItem(todo));
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

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && _accessExpiresUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _accessToken;
        }

        var refreshToken = _secrets.Get(SecretKeys.GitLabRefreshToken);
        if (refreshToken is null)
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ApplicationId,
        };

        try
        {
            var (status, token) = await PostTokenFormAsync(form, ct).ConfigureAwait(false);
            if (status != HttpStatusCode.OK || string.IsNullOrEmpty(token?.AccessToken))
            {
                _logger.LogWarning("Failed to refresh the GitLab access token ({Status}).", status);
                return null;
            }

            StoreTokens(token);
            return _accessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to refresh the GitLab access token.");
            return null;
        }
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

    private async Task<GitLabUser?> GetCurrentUserAsync(CancellationToken ct)
    {
        using var response = await SendWithAuthAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/user"), ct).ConfigureAwait(false);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<GitLabUser>(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private void StoreTokens(GitLabTokenResponse token)
    {
        _accessToken = token.AccessToken;
        _accessExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60));

        if (!string.IsNullOrEmpty(token.AccessToken))
        {
            _secrets.Set(SecretKeys.GitLabAccessToken, token.AccessToken);
        }

        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            _secrets.Set(SecretKeys.GitLabRefreshToken, token.RefreshToken);
        }
    }

    private void RestoreSession()
    {
        if (_secrets.Contains(SecretKeys.GitLabRefreshToken))
        {
            IsConnected = true;
            AccountLogin = _settings.Load().GitLab.AccountLogin;
        }
    }

    private void PersistConnectionState(bool connected, string? login)
    {
        var settings = _settings.Load();
        settings.GitLab.Connected = connected;
        settings.GitLab.AccountLogin = login;
        _settings.Save(settings);
    }
}
