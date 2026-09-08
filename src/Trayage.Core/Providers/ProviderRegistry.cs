using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Security;

namespace Trayage.Core.Providers;

/// <summary>
/// The live set of provider instances, one per configured account. Replaces the old fixed
/// per-provider DI singletons: accounts come and go at runtime, so the inbox pipeline queries
/// this registry on every refresh rather than capturing a list at construction.
/// </summary>
public sealed class ProviderRegistry(
    IProviderFactory factory,
    ISettingsStore settings,
    ISecretStore secrets,
    ILogger<ProviderRegistry> logger)
{
    private readonly Lock _gate = new();
    private readonly List<IInboxProvider> _providers = new();

    /// <summary>Raised after a provider is added or removed, so the UI and tray can re-read state.</summary>
    public event EventHandler? Changed;

    /// <summary>A snapshot of every provider instance, including accounts that are paused.</summary>
    public IReadOnlyList<IInboxProvider> All
    {
        get
        {
            lock (_gate)
            {
                return _providers.ToList();
            }
        }
    }

    /// <summary>
    /// The providers a refresh should query: connected, and belonging to an account the user
    /// hasn't paused.
    /// </summary>
    public IReadOnlyList<IInboxProvider> Active
    {
        get
        {
            var enabled = settings.Load().Accounts
                .Where(a => a.Enabled)
                .Select(a => a.Id)
                .ToHashSet(StringComparer.Ordinal);

            return All.Where(p => p.IsConnected && enabled.Contains(p.AccountId)).ToList();
        }
    }

    /// <summary>Builds the initial set from saved settings. Call once, after migration.</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            _providers.Clear();
            foreach (var account in settings.Load().Accounts)
            {
                TryAdd(account);
            }

            logger.LogInformation("Initialised {Count} provider account(s).", _providers.Count);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Registers a newly added account so the very next refresh includes it.</summary>
    public IInboxProvider Add(ProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        IInboxProvider provider;
        lock (_gate)
        {
            provider = factory.Create(account);
            _providers.Add(provider);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return provider;
    }

    /// <summary>
    /// Drops an account: removes its provider, deletes its row from settings, and purges its
    /// tokens so nothing is left behind in <c>secrets.dat</c>.
    /// </summary>
    public void Remove(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            _providers.RemoveAll(p => string.Equals(p.AccountId, accountId, StringComparison.Ordinal));

            var current = settings.Load();
            if (current.FindAccount(accountId) is { } account)
            {
                foreach (var key in SecretKeys.AllFor(account.Provider, account.Id))
                {
                    secrets.Remove(key);
                }

                current.Accounts.RemoveAll(a => string.Equals(a.Id, accountId, StringComparison.Ordinal));
                settings.Save(current);
            }
        }

        logger.LogInformation("Removed account {AccountId}.", accountId);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The provider serving an account, or null when it isn't registered.</summary>
    public IInboxProvider? Find(string accountId) =>
        All.FirstOrDefault(p => string.Equals(p.AccountId, accountId, StringComparison.Ordinal));

    private void TryAdd(ProviderAccount account)
    {
        try
        {
            _providers.Add(factory.Create(account));
        }
        catch (Exception ex)
        {
            // A malformed account row shouldn't stop the others from loading.
            logger.LogError(ex, "Couldn't create a provider for account {AccountId}.", account.Id);
        }
    }
}
