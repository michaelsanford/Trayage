using Trayage.Core.Providers;
using Trayage.Core.Providers.GitLab;

namespace Trayage.Core.Tests;

/// <summary>
/// The instance URL carries device codes, OAuth tokens and bearer-authenticated API calls,
/// so it is always forced onto https whatever scheme was configured.
/// </summary>
public sealed class GitLabInstanceUrlTests
{
    [Theory]
    [InlineData("https://gitlab.com", "https://gitlab.com")]
    [InlineData("https://gitlab.com/", "https://gitlab.com")]
    [InlineData("http://gitlab.example.com", "https://gitlab.example.com")]
    [InlineData("HTTP://gitlab.example.com/", "https://gitlab.example.com")]
    [InlineData("http://gitlab.example.com:8080/", "https://gitlab.example.com:8080")]
    [InlineData("ftp://gitlab.example.com", "https://gitlab.example.com")]
    [InlineData("gitlab.example.com", "https://gitlab.example.com")]
    [InlineData("  http://gitlab.example.com/gitlab/  ", "https://gitlab.example.com/gitlab")]
    [InlineData("http://gitlab.example.com/?x=1#frag", "https://gitlab.example.com")]
    public void NormalizeInstanceUrl_ForcesHttps(string configured, string expected) =>
        Assert.Equal(expected, GitLabProvider.NormalizeInstanceUrl(configured));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("https:// /")]
    public void NormalizeInstanceUrl_RejectsUnusableValues(string configured) =>
        Assert.Throws<ProviderNotConfiguredException>(() => GitLabProvider.NormalizeInstanceUrl(configured));
}
