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
}
