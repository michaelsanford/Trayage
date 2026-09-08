using Trayage.Core.Configuration;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Providers;

/// <summary>
/// Binds a provider instance to the one account it serves: where its tokens live, which row of
/// <see cref="TrayageSettings.Accounts"/> it reads and writes, and which repositories it watches.
/// Providers hold one of these instead of reaching for a fixed per-provider settings slot, which
/// is what lets several accounts of the same provider run side by side.
/// </summary>
public sealed class ProviderAccountContext(
    ProviderKind provider,
    string accountId,
    ISettingsStore settings,
    ISecretStore secrets)
{
    public ProviderKind Provider { get; } = provider;

    public string AccountId { get; } = accountId;

    public string AccessTokenKey => SecretKeys.AccessToken(Provider, AccountId);

    public string RefreshTokenKey => SecretKeys.RefreshToken(Provider, AccountId);

    /// <summary>This account's persisted row, or null once it has been removed.</summary>
    public ProviderAccount? Load() => settings.Load().FindAccount(AccountId);

    public string? AccountLogin => Load()?.AccountLogin;

    /// <summary>"GitHub · Work" — used in toasts and error text to name the failing account.</summary>
    public string QualifiedLabel => Load()?.QualifiedLabel ?? Provider.DisplayName();

    /// <summary>The instance this account lives on, or null for the provider's public cloud.</summary>
    public string? BaseUrl => Load()?.BaseUrl;

    /// <summary>Repositories watched by <em>this</em> account, not the app as a whole.</summary>
    public IReadOnlyList<string> WatchedRepositories =>
        Load()?.WatchedRepositories ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>
    /// Records the outcome of a connect/disconnect. Re-loads first so a concurrent settings
    /// write (the Settings window persists on every change) isn't clobbered.
    /// </summary>
    public void PersistConnection(bool connected, string? login)
    {
        var current = settings.Load();
        var account = current.FindAccount(AccountId);
        if (account is null)
        {
            return;
        }

        account.Connected = connected;
        account.AccountLogin = login;
        settings.Save(current);
    }

    /// <summary>Deletes every token this account owns. Called on disconnect and on removal.</summary>
    public void PurgeSecrets()
    {
        foreach (var key in SecretKeys.AllFor(Provider, AccountId))
        {
            secrets.Remove(key);
        }
    }
}
