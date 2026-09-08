using Trayage.Core.Models;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

public sealed class SecretKeysTests
{
    [Fact]
    public void Keys_AreScopedPerAccount()
    {
        // The whole point: two accounts on one provider must not overwrite each other's token.
        Assert.NotEqual(
            SecretKeys.AccessToken(ProviderKind.GitHub, "work"),
            SecretKeys.AccessToken(ProviderKind.GitHub, "personal"));
    }

    [Fact]
    public void Keys_AreScopedPerProvider()
    {
        Assert.NotEqual(
            SecretKeys.AccessToken(ProviderKind.GitHub, "a"),
            SecretKeys.AccessToken(ProviderKind.GitLab, "a"));
    }

    [Fact]
    public void AccessAndRefreshKeys_Differ()
    {
        Assert.NotEqual(
            SecretKeys.AccessToken(ProviderKind.Bitbucket, "a"),
            SecretKeys.RefreshToken(ProviderKind.Bitbucket, "a"));
    }

    [Theory]
    [InlineData(ProviderKind.GitHub, "github")]
    [InlineData(ProviderKind.Bitbucket, "bitbucket")]
    [InlineData(ProviderKind.GitLab, "gitlab")]
    public void Keys_AreStableAndReadable(ProviderKind provider, string slug)
    {
        // secrets.dat is inspectable by the user, so the keys should stay legible — and stable,
        // because changing the format would orphan every stored token.
        Assert.Equal($"{slug}.acct1.access_token", SecretKeys.AccessToken(provider, "acct1"));
    }

    [Fact]
    public void AllFor_CoversBothKeys_SoRemovalLeavesNothingBehind()
    {
        var keys = SecretKeys.AllFor(ProviderKind.GitLab, "acct1").ToList();

        Assert.Contains(SecretKeys.AccessToken(ProviderKind.GitLab, "acct1"), keys);
        Assert.Contains(SecretKeys.RefreshToken(ProviderKind.GitLab, "acct1"), keys);
    }

    [Fact]
    public void EmptyAccountId_Throws_RatherThanCollidingOnAGlobalKey()
    {
        Assert.Throws<ArgumentException>(() => SecretKeys.AccessToken(ProviderKind.GitHub, ""));
    }

    [Theory]
    [InlineData(ProviderKind.GitHub)]
    [InlineData(ProviderKind.Bitbucket)]
    [InlineData(ProviderKind.GitLab)]
    public void LegacyKeys_AreStillReadable_ForMigration(ProviderKind provider)
    {
        var (access, _) = SecretKeys.Legacy.For(provider);

        Assert.False(string.IsNullOrEmpty(access));
    }
}
