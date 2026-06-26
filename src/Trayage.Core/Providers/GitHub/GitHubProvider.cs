using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Providers.GitHub;

/// <summary>
/// GitHub inbox provider. Authenticates with the OAuth device flow and reads the
/// authenticated user's notification inbox, translating each thread into an
/// <see cref="InboxItem"/>.
/// </summary>
public sealed class GitHubProvider : IInboxProvider
{
    private static readonly ProductHeaderValue Product = new("Trayage");

    private readonly GitHubOptions _options;
    private readonly ISecretStore _secrets;
    private readonly ISettingsStore _settings;
    private readonly ILogger<GitHubProvider> _logger;
    private readonly GitHubClient _client;

    public GitHubProvider(
        IOptions<GitHubOptions> options,
        ISecretStore secrets,
        ISettingsStore settings,
        ILogger<GitHubProvider> logger)
    {
        _options = options.Value;
        _secrets = secrets;
        _settings = settings;
        _logger = logger;
        _client = new GitHubClient(Product);

        RestoreSession();
    }

    public ProviderKind Provider => ProviderKind.GitHub;

    public bool IsConnected { get; private set; }

    public string? AccountLogin { get; private set; }

    /// <summary>Set from the server's <c>X-Poll-Interval</c> after each fetch; null until known.</summary>
    public TimeSpan? SuggestedPollInterval { get; private set; }

    /// <summary>
    /// Runs the OAuth device flow. <paramref name="onPromptReady"/> is invoked with the
    /// user code and verification URL (show it and open the browser); the returned task
    /// then polls GitHub until the user authorises or the code expires.
    /// </summary>
    public async Task ConnectAsync(Func<DeviceCodePrompt, Task> onPromptReady, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            throw new ProviderNotConfiguredException(
                "No GitHub OAuth client id is configured. Set GitHub:ClientId in appsettings.json.");
        }

        var request = new OauthDeviceFlowRequest(_options.ClientId);
        foreach (var scope in _options.Scopes)
        {
            request.Scopes.Add(scope);
        }

        var device = await _client.Oauth.InitiateDeviceFlow(request, cancellationToken).ConfigureAwait(false);

        await onPromptReady(new DeviceCodePrompt(
            device.UserCode,
            device.VerificationUri,
            TimeSpan.FromSeconds(device.ExpiresIn))).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var token = await _client.Oauth.CreateAccessTokenForDeviceFlow(_options.ClientId, device, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException("GitHub did not return an access token.");
        }

        _secrets.Set(SecretKeys.GitHubAccessToken, token.AccessToken);
        _client.Credentials = new Credentials(token.AccessToken);

        var user = await _client.User.Current().ConfigureAwait(false);
        AccountLogin = user.Login;
        IsConnected = true;

        PersistConnectionState(connected: true, login: user.Login);
        // Note: the account login is PII, so it is not written to the log.
        _logger.LogInformation("Connected to GitHub.");
    }

    public void Disconnect()
    {
        _secrets.Remove(SecretKeys.GitHubAccessToken);
        _client.Credentials = Credentials.Anonymous;
        IsConnected = false;
        AccountLogin = null;
        PersistConnectionState(connected: false, login: null);
        _logger.LogInformation("Disconnected from GitHub.");
    }

    public async Task<IReadOnlyList<InboxItem>> FetchInboxAsync(InboxQuery query, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return Array.Empty<InboxItem>();
        }

        // Read and unread, mirroring the GitHub notifications inbox; the unread flag drives
        // the read/unread mark in the UI and gates toasts.
        var request = new NotificationsRequest { All = true };
        var notifications = await _client.Activity.Notifications
            .GetAllForCurrent(request)
            .ConfigureAwait(false);

        _logger.LogDebug("Fetched {Count} GitHub notification(s).", notifications.Count);

        // Octokit doesn't surface the X-Poll-Interval header, so we keep a conservative
        // floor; the app default poll interval governs cadence from settings.
        SuggestedPollInterval ??= TimeSpan.FromSeconds(60);

        var items = new List<InboxItem>(notifications.Count);
        foreach (var n in notifications)
        {
            items.Add(Map(n));
        }

        return items;
    }

    private static InboxItem Map(Notification n)
    {
        var repoHtmlUrl = n.Repository?.HtmlUrl ?? "https://github.com";
        var webUrl = GitHubWebUrl.Build(n.Subject?.Url, n.Subject?.Type, repoHtmlUrl);

        return new InboxItem
        {
            Id = n.Id,
            Provider = ProviderKind.GitHub,
            Kind = GitHubReasonMapper.ToKind(n.Reason),
            Title = n.Subject?.Title ?? "(no title)",
            RepositoryFullName = n.Repository?.FullName ?? "unknown/unknown",
            Reason = n.Reason ?? string.Empty,
            WebUrl = webUrl,
            UpdatedAt = ParseTimestamp(n.UpdatedAt),
            IsUnread = n.Unread,
        };
    }

    private static DateTimeOffset ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private void RestoreSession()
    {
        var token = _secrets.Get(SecretKeys.GitHubAccessToken);
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        _client.Credentials = new Credentials(token);
        IsConnected = true;
        AccountLogin = _settings.Load().GitHub.AccountLogin;
    }

    private void PersistConnectionState(bool connected, string? login)
    {
        var settings = _settings.Load();
        settings.GitHub.Connected = connected;
        settings.GitHub.AccountLogin = login;
        _settings.Save(settings);
    }
}
