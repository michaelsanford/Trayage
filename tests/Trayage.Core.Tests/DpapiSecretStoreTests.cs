using Microsoft.Extensions.Logging.Abstractions;
using Trayage.Core.Security;

namespace Trayage.Core.Tests;

/// <summary>
/// Exercises the real DPAPI store. These run on Windows (the only supported OS for the
/// app) using a temp-file backing path so they don't touch real user data.
/// </summary>
public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trayage-secrets-{Guid.NewGuid():N}.dat");

    private DpapiSecretStore NewStore() => new(NullLogger<DpapiSecretStore>.Instance, _path);

    [Fact]
    public void SetThenGet_RoundTrips()
    {
        var store = NewStore();
        store.Set("github.access", "gho_secrettoken");

        Assert.Equal("gho_secrettoken", store.Get("github.access"));
        Assert.True(store.Contains("github.access"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        Assert.Null(NewStore().Get("nope"));
    }

    [Fact]
    public void Remove_DeletesSecret()
    {
        var store = NewStore();
        store.Set("k", "v");
        store.Remove("k");

        Assert.False(store.Contains("k"));
        Assert.Null(store.Get("k"));
    }

    [Fact]
    public void StoredFile_DoesNotContainPlaintext()
    {
        NewStore().Set("token", "super-secret-value-123");

        var onDisk = File.ReadAllText(_path);
        Assert.DoesNotContain("super-secret-value-123", onDisk);
    }

    [Fact]
    public void Values_SurviveAcrossStoreInstances()
    {
        NewStore().Set("refresh", "r3fr3sh");

        Assert.Equal("r3fr3sh", NewStore().Get("refresh"));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
