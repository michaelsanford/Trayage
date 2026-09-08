using Trayage.Core.Inbox;

namespace Trayage.Core.Tests;

public sealed class RepositoryReferenceTests
{
    [Theory]
    [InlineData("acme/widgets", "acme/widgets")]
    [InlineData("  acme/widgets  ", "acme/widgets")]
    [InlineData("acme/widgets/", "acme/widgets")]
    [InlineData("https://bitbucket.org/acme/widgets", "acme/widgets")]
    [InlineData("https://bitbucket.org/acme/widgets/pull-requests/42", "acme/widgets")]
    [InlineData("bitbucket.org/acme/widgets", "acme/widgets")]
    [InlineData("https://github.com/acme/widgets", "acme/widgets")]
    [InlineData("https://github.com/acme/widgets.git", "acme/widgets")]
    [InlineData("https://bitbucket.org/acme/widgets?at=main", "acme/widgets")]
    [InlineData("https://bitbucket.org/acme/widgets#readme", "acme/widgets")]
    public void Normalize_ReducesToOwnerRepo(string input, string expected)
    {
        Assert.Equal(expected, RepositoryReference.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("acme")]
    [InlineData("/widgets")]
    [InlineData("acme/")]
    [InlineData("acme widgets")]
    [InlineData("https://bitbucket.org/acme")]
    public void Normalize_RejectsIncompleteInput(string? input)
    {
        Assert.Null(RepositoryReference.Normalize(input));
    }

    [Theory]
    [InlineData("acme/widgets", "acme", "widgets")]
    // Only the first slash separates owner from repo; the rest belongs to the name, which is
    // what GitLab subgroups look like.
    [InlineData("acme/group/widgets", "acme", "group/widgets")]
    public void Split_SeparatesOwnerFromRepository(string input, string owner, string name)
    {
        Assert.Equal((owner, name), RepositoryReference.Split(input));
    }

    /// <summary>
    /// The inbox also groups by recency bucket, whose header is a plain word. Those come back
    /// whole as the name so the flyout can render either shape without special-casing.
    /// </summary>
    [Theory]
    [InlineData("Today", "Today")]
    [InlineData("acme", "acme")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Split_WithoutASlash_ReturnsNoOwner(string? input, string name)
    {
        Assert.Equal((string.Empty, name), RepositoryReference.Split(input));
    }
}
