using Microsoft.Extensions.Logging.Abstractions;
using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

/// <summary>
/// Covers the one-way upgrade from the pre-accounts settings shape. The contract that matters to
/// a user is "nothing to reconnect": a connection recorded in a legacy slot must come out the
/// other side as an account whose token is readable under its new key.
/// </summary>
public sealed class SettingsMigrationTests
{
    /// <summary>An in-memory settings store, so migration can be driven end to end.</summary>
    private sealed class FakeSettingsStore : ISettingsStore
    {
        private TrayageSettings _settings = new();

        public string FilePath => "(memory)";

        public int SaveCount { get; private set; }

        public TrayageSettings Load() => _settings.Clone();

        public void Save(TrayageSettings settings)
        {
            _settings = settings.Clone();
            SaveCount++;
        }
    }

    private readonly FakeSettingsStore _settings = new();
    private readonly InMemorySecretStore _secrets = new();

    private bool Run() => SettingsMigration.Run(_settings, _secrets, NullLogger.Instance);

    private void SeedLegacyGitHub(string login = "octocat", string token = "gh-token")
    {
        var settings = _settings.Load();
        settings.GitHub.Connected = true;
        settings.GitHub.AccountLogin = login;
        _settings.Save(settings);
        _secrets.Set(SecretKeys.Legacy.GitHubAccessToken, token);
    }

    [Fact]
    public void Run_LegacyConnection_BecomesAnAccount()
    {
        SeedLegacyGitHub();

        Assert.True(Run());

        var account = Assert.Single(_settings.Load().Accounts);
        Assert.Equal(ProviderKind.GitHub, account.Provider);
        Assert.Equal("octocat", account.AccountLogin);
        Assert.True(account.Connected);
        Assert.True(account.Enabled);
    }

    [Fact]
    public void Run_ReKeysTheTokenSoNoReconnectIsNeeded()
    {
        SeedLegacyGitHub(token: "gh-token");

        Run();

        var account = Assert.Single(_settings.Load().Accounts);
        Assert.Equal("gh-token", _secrets.Get(SecretKeys.AccessToken(ProviderKind.GitHub, account.Id)));
        Assert.Null(_secrets.Get(SecretKeys.Legacy.GitHubAccessToken));
    }

    [Fact]
    public void Run_MovesRefreshTokensToo()
    {
        var settings = _settings.Load();
        settings.Bitbucket.Connected = true;
        settings.Bitbucket.AccountLogin = "someone";
        _settings.Save(settings);
        _secrets.Set(SecretKeys.Legacy.BitbucketAccessToken, "bb-access");
        _secrets.Set(SecretKeys.Legacy.BitbucketRefreshToken, "bb-refresh");

        Run();

        var account = Assert.Single(_settings.Load().Accounts);
        Assert.Equal("bb-access", _secrets.Get(SecretKeys.AccessToken(ProviderKind.Bitbucket, account.Id)));
        Assert.Equal("bb-refresh", _secrets.Get(SecretKeys.RefreshToken(ProviderKind.Bitbucket, account.Id)));
        Assert.Null(_secrets.Get(SecretKeys.Legacy.BitbucketRefreshToken));
    }

    [Fact]
    public void Run_GivesTheGlobalWatchedListToBitbucketOnly()
    {
        var settings = _settings.Load();
        settings.GitHub.Connected = true;
        settings.Bitbucket.Connected = true;
        settings.WatchedRepositories.Add("acme/widgets");
        _settings.Save(settings);

        Run();

        var accounts = _settings.Load().Accounts;
        var bitbucket = accounts.Single(a => a.Provider == ProviderKind.Bitbucket);
        var gitHub = accounts.Single(a => a.Provider == ProviderKind.GitHub);

        // Bitbucket is the only provider that queries per repository; GitHub reads a
        // server-side inbox, so a watched list there would mean nothing.
        Assert.Equal(new[] { "acme/widgets" }, bitbucket.WatchedRepositories);
        Assert.Empty(gitHub.WatchedRepositories);
    }

    [Fact]
    public void Run_NeverConnectedProvider_ProducesNoAccount()
    {
        SeedLegacyGitHub();

        Run();

        // Only GitHub was ever connected — no empty Bitbucket or GitLab rows.
        Assert.Single(_settings.Load().Accounts);
    }

    [Fact]
    public void Run_IsIdempotent()
    {
        SeedLegacyGitHub();

        Assert.True(Run());
        var afterFirst = _settings.Load();
        Assert.Equal(SettingsMigration.CurrentSchemaVersion, afterFirst.SchemaVersion);

        Assert.False(Run());
        Assert.Single(_settings.Load().Accounts);
    }

    [Fact]
    public void Run_AlreadyCurrent_LeavesAccountsUntouched()
    {
        var settings = _settings.Load();
        settings.SchemaVersion = SettingsMigration.CurrentSchemaVersion;
        settings.Accounts.Add(new ProviderAccount { Id = "existing", Provider = ProviderKind.GitLab });
        // A stale legacy slot must not be resurrected into a duplicate account.
        settings.GitHub.Connected = true;
        _settings.Save(settings);

        Assert.False(Run());

        var account = Assert.Single(_settings.Load().Accounts);
        Assert.Equal("existing", account.Id);
    }

    [Fact]
    public void Run_FreshInstall_JustStampsTheVersion()
    {
        Assert.True(Run());

        var settings = _settings.Load();
        Assert.Empty(settings.Accounts);
        Assert.Equal(SettingsMigration.CurrentSchemaVersion, settings.SchemaVersion);
    }
}
