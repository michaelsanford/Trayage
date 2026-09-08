using Microsoft.Extensions.Logging;
using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Configuration;

/// <summary>
/// Upgrades a pre-accounts <c>settings.json</c> (and its <c>secrets.dat</c>) to the
/// multi-account shape, in place and without asking the user to reconnect anything.
/// Idempotent: it runs once, stamps <see cref="TrayageSettings.SchemaVersion"/>, and no-ops
/// on every later launch.
/// </summary>
public static class SettingsMigration
{
    /// <summary>The shape this build writes. Bump alongside a new migration step.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Schema that introduced multi-account support.</summary>
    private const int AccountsSchemaVersion = 1;

    /// <summary>Schema that replaced the GroupByRepository flag with <see cref="InboxGrouping"/>.</summary>
    private const int GroupingSchemaVersion = 2;

    /// <summary>
    /// Migrates if needed and returns true when something was written. Must run before any
    /// provider is constructed, since providers read accounts and account-scoped secret keys.
    /// </summary>
    public static bool Run(ISettingsStore settings, ISecretStore secrets, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);

        var current = settings.Load();
        if (current.SchemaVersion >= CurrentSchemaVersion)
        {
            return false;
        }

        // Each step is gated on its own version, not on the outer check, so a file that is
        // merely behind by one schema doesn't re-run the steps it already went through.
        // A file that already has accounts (or is brand new) only needs the stamp.
        if (current.SchemaVersion < AccountsSchemaVersion && current.Accounts.Count == 0)
        {
            MigrateLegacyAccounts(current, secrets, logger);
        }

        if (current.SchemaVersion < GroupingSchemaVersion)
        {
            MigrateGrouping(current, logger);
        }

        current.SchemaVersion = CurrentSchemaVersion;
        settings.Save(current);
        return true;
    }

    /// <summary>
    /// Translates the old two-way <c>GroupByRepository</c> flag into <see cref="InboxGrouping"/>.
    /// False meant the flat, newest-first list, which is now <see cref="InboxGrouping.Time"/>;
    /// true (and a file that never had the flag) means <see cref="InboxGrouping.Repository"/>,
    /// the old default. Nulling the legacy field drops it from the next save.
    /// </summary>
    private static void MigrateGrouping(TrayageSettings settings, ILogger? logger)
    {
        if (settings.GroupByRepository is not { } groupByRepository)
        {
            return;
        }

        settings.Grouping = groupByRepository ? InboxGrouping.Repository : InboxGrouping.Time;
        settings.GroupByRepository = null;
        logger?.LogInformation("Migrated legacy GroupByRepository flag to grouping {Grouping}.", settings.Grouping);
    }

    private static void MigrateLegacyAccounts(TrayageSettings settings, ISecretStore secrets, ILogger? logger)
    {
        foreach (var provider in new[] { ProviderKind.GitHub, ProviderKind.Bitbucket, ProviderKind.GitLab })
        {
            var legacy = LegacySlot(settings, provider);

            // Nothing was ever connected for this provider — no account to carry forward.
            if (!legacy.Connected && string.IsNullOrEmpty(legacy.AccountLogin))
            {
                continue;
            }

            var account = new ProviderAccount
            {
                Id = ProviderAccount.NewId(),
                Provider = provider,
                AccountLogin = legacy.AccountLogin,
                Connected = legacy.Connected,
                // Bitbucket is the only provider that queries per repository, so it inherits
                // the old global watched list; GitHub and GitLab read a server-side inbox.
                WatchedRepositories = provider == ProviderKind.Bitbucket
                    ? new List<string>(settings.WatchedRepositories)
                    : new List<string>(),
            };

            settings.Accounts.Add(account);
            ReKeySecrets(provider, account.Id, secrets);

            // The login is PII, so it stays out of the log.
            logger?.LogInformation("Migrated legacy {Provider} connection to account {AccountId}.", provider, account.Id);
        }
    }

    /// <summary>Moves a provider's flat tokens onto the account-scoped keys, dropping the originals.</summary>
    private static void ReKeySecrets(ProviderKind provider, string accountId, ISecretStore secrets)
    {
        var (legacyAccess, legacyRefresh) = SecretKeys.Legacy.For(provider);

        Move(legacyAccess, SecretKeys.AccessToken(provider, accountId));
        if (legacyRefresh is not null)
        {
            Move(legacyRefresh, SecretKeys.RefreshToken(provider, accountId));
        }

        void Move(string from, string to)
        {
            if (secrets.Get(from) is { Length: > 0 } value)
            {
                secrets.Set(to, value);
                secrets.Remove(from);
            }
        }
    }

    private static ProviderConnectionState LegacySlot(TrayageSettings settings, ProviderKind provider) => provider switch
    {
        ProviderKind.GitHub => settings.GitHub,
        ProviderKind.Bitbucket => settings.Bitbucket,
        ProviderKind.GitLab => settings.GitLab,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
}
