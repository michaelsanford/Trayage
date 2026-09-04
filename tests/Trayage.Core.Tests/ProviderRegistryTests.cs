using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Models;
using Trayage.Core.Providers;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

/// <summary>
/// The registry replaced a fixed list captured at construction, so the behaviour worth pinning
/// is that a newly added account is visible to the very next refresh — no restart.
/// </summary>
public sealed class ProviderRegistryTests
{
    private readonly ISettingsStore _settings = Substitute.For<ISettingsStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly TrayageSettings _stored = new();

    public ProviderRegistryTests() => _settings.Load().Returns(_stored);

    private ProviderRegistry NewRegistry(IProviderFactory factory) =>
        new(factory, _settings, _secrets, NullLogger<ProviderRegistry>.Instance);

    /// <summary>A factory that mints a stub provider matching whichever account it is handed.</summary>
    private static IProviderFactory Factory()
    {
        var factory = Substitute.For<IProviderFactory>();
        factory.Create(Arg.Any<ProviderAccount>()).Returns(call =>
        {
            var account = call.Arg<ProviderAccount>();
            return TestProviders.Stub(account.Provider, account.Id);
        });
        return factory;
    }

    private ProviderAccount AddAccount(string id, ProviderKind provider = ProviderKind.GitHub)
    {
        var account = new ProviderAccount { Id = id, Provider = provider, Connected = true };
        _stored.Accounts.Add(account);
        return account;
    }

    [Fact]
    public void Initialize_CreatesOneProviderPerAccount()
    {
        AddAccount("a");
        AddAccount("b", ProviderKind.GitLab);
        var registry = NewRegistry(Factory());

        registry.Initialize();

        Assert.Equal(new[] { "a", "b" }, registry.All.Select(p => p.AccountId));
    }

    [Fact]
    public void Initialize_TwoAccountsOnOneProvider_BothRegister()
    {
        AddAccount("work");
        AddAccount("personal");
        var registry = NewRegistry(Factory());

        registry.Initialize();

        Assert.Equal(2, registry.All.Count(p => p.Provider == ProviderKind.GitHub));
    }

    [Fact]
    public void Add_IsVisibleImmediately_WithoutReinitialising()
    {
        var registry = NewRegistry(Factory());
        registry.Initialize();
        Assert.Empty(registry.All);

        registry.Add(AddAccount("late"));

        Assert.Equal("late", Assert.Single(registry.All).AccountId);
    }

    [Fact]
    public void Add_RaisesChanged_SoTheTrayCanRepaint()
    {
        var registry = NewRegistry(Factory());
        registry.Initialize();
        var raised = 0;
        registry.Changed += (_, _) => raised++;

        registry.Add(AddAccount("a"));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Remove_DropsTheProviderAndItsSettingsRow()
    {
        AddAccount("a");
        AddAccount("b");
        var registry = NewRegistry(Factory());
        registry.Initialize();

        registry.Remove("a");

        Assert.Equal("b", Assert.Single(registry.All).AccountId);
        Assert.Null(_stored.FindAccount("a"));
    }

    [Fact]
    public void Remove_PurgesTheAccountsTokens_SoNothingIsOrphaned()
    {
        AddAccount("a");
        var registry = NewRegistry(Factory());
        registry.Initialize();

        registry.Remove("a");

        _secrets.Received().Remove(SecretKeys.AccessToken(ProviderKind.GitHub, "a"));
        _secrets.Received().Remove(SecretKeys.RefreshToken(ProviderKind.GitHub, "a"));
    }

    [Fact]
    public void Remove_LeavesTheOtherAccountsTokensAlone()
    {
        AddAccount("a");
        AddAccount("b");
        var registry = NewRegistry(Factory());
        registry.Initialize();

        registry.Remove("a");

        _secrets.DidNotReceive().Remove(SecretKeys.AccessToken(ProviderKind.GitHub, "b"));
    }

    [Fact]
    public void Active_ExcludesPausedAccounts_ButAllStillListsThem()
    {
        AddAccount("on");
        AddAccount("off").Enabled = false;
        var registry = NewRegistry(Factory());
        registry.Initialize();

        Assert.Equal("on", Assert.Single(registry.Active).AccountId);
        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public void Find_ReturnsNull_ForAnUnknownAccount()
    {
        var registry = NewRegistry(Factory());
        registry.Initialize();

        Assert.Null(registry.Find("nope"));
    }

    [Fact]
    public void Initialize_OneBadAccount_DoesNotStopTheOthers()
    {
        AddAccount("bad");
        AddAccount("good");
        var factory = Substitute.For<IProviderFactory>();
        factory.Create(Arg.Any<ProviderAccount>()).Returns(call =>
        {
            var account = call.Arg<ProviderAccount>();
            if (account.Id == "bad")
            {
                throw new InvalidOperationException("unsupported");
            }

            return TestProviders.Stub(account.Provider, account.Id);
        });

        var registry = NewRegistry(factory);
        registry.Initialize();

        Assert.Equal("good", Assert.Single(registry.All).AccountId);
    }
}
