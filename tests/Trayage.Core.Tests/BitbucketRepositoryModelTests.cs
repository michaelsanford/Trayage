using System.Text.Json;
using Trayage.Core.Providers.Bitbucket;

namespace Trayage.Core.Tests;

public sealed class BitbucketRepositoryModelTests
{
    [Fact]
    public void PagedRepositories_DeserializesValuesAndNext()
    {
        const string json = """
        {
          "next": "https://api.bitbucket.org/2.0/repositories/acme?page=2",
          "values": [
            { "full_name": "acme/widgets", "name": "widgets", "updated_on": "2026-02-03T10:00:00.000000+00:00" },
            { "full_name": "acme/gadgets", "name": "gadgets", "updated_on": "2026-01-01T00:00:00.000000+00:00" }
          ]
        }
        """;

        var page = JsonSerializer.Deserialize<BitbucketPagedRepositories>(json);

        Assert.NotNull(page);
        Assert.Equal("https://api.bitbucket.org/2.0/repositories/acme?page=2", page.Next);
        Assert.Equal(2, page.Values.Count);
        Assert.Equal("acme/widgets", page.Values[0].FullName);
        Assert.Equal("widgets", page.Values[0].Name);
    }

    [Fact]
    public void UserWorkspaces_DeserializeInlineSlug()
    {
        const string json = """{ "values": [ { "slug": "acme" }, { "slug": "jdoe" } ] }""";

        var page = JsonSerializer.Deserialize<BitbucketPagedUserWorkspaces>(json);

        Assert.NotNull(page);
        Assert.Equal("acme", page.Values[0].EffectiveSlug);
        Assert.Equal("jdoe", page.Values[1].EffectiveSlug);
    }

    [Fact]
    public void UserWorkspaces_DeserializeNestedWorkspaceSlug()
    {
        // Membership-style shape: the slug lives under a nested "workspace" object.
        const string json = """{ "values": [ { "workspace": { "slug": "acme" } } ] }""";

        var page = JsonSerializer.Deserialize<BitbucketPagedUserWorkspaces>(json);

        Assert.NotNull(page);
        Assert.Equal("acme", page.Values[0].EffectiveSlug);
    }
}
