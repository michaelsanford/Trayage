using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

/// <summary>
/// Builds stub providers and the <see cref="ProviderRegistry"/> that owns them, so tests can
/// exercise the inbox pipeline without standing up real OAuth providers.
/// </summary>
internal static class TestProviders
{
    /// <summary>A stub provider bound to an account id, returning <paramref name="items"/> on every fetch.</summary>
    public static IInboxProvider Provider(
        ProviderKind kind = ProviderKind.GitHub,
        string accountId = "acct1",
        bool connected = true,
        TimeSpan? suggestedPollInterval = null,
        params InboxItem[] items)
    {
        var provider = Stub(kind, accountId, connected, suggestedPollInterval);
        provider.FetchInboxAsync(Arg.Any<InboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<InboxItem>>(items));
        return provider;
    }

    /// <summary>A stub provider with no fetch behaviour configured; the caller sets it up.</summary>
    public static IInboxProvider Stub(
        ProviderKind kind = ProviderKind.GitHub,
        string accountId = "acct1",
        bool connected = true,
        TimeSpan? suggestedPollInterval = null)
    {
        var provider = Substitute.For<IInboxProvider>();
        provider.Provider.Returns(kind);
        provider.AccountId.Returns(accountId);
        provider.IsConnected.Returns(connected);
        provider.SuggestedPollInterval.Returns(suggestedPollInterval);
        provider.DisplayLabel.Returns($"{kind.DisplayName()} · {accountId}");
        return provider;
    }

    /// <summary>
    /// Registers an account row for each provider (unless one already exists) and returns a
    /// registry wired to hand those providers back. Mirrors what the app does at startup.
    /// </summary>
    public static ProviderRegistry Registry(ISettingsStore settings, params IInboxProvider[] providers)
    {
        var current = settings.Load();
        foreach (var provider in providers)
        {
            if (current.FindAccount(provider.AccountId) is null)
            {
                current.Accounts.Add(new ProviderAccount
                {
                    Id = provider.AccountId,
                    Provider = provider.Provider,
                    Connected = provider.IsConnected,
                });
            }
        }

        settings.Save(current);

        var factory = Substitute.For<IProviderFactory>();
        factory.Create(Arg.Any<ProviderAccount>()).Returns(call =>
        {
            var account = call.Arg<ProviderAccount>();
            return providers.First(p => p.AccountId == account.Id);
        });

        var registry = new ProviderRegistry(
            factory, settings, Substitute.For<ISecretStore>(), NullLogger<ProviderRegistry>.Instance);
        registry.Initialize();
        return registry;
    }
}
