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

    [Fact]
    public void CorruptFile_IsMovedAside_AndStoreStartsEmpty()
    {
        File.WriteAllText(_path, "{ not json");
        var store = NewStore();

        Assert.Null(store.Get("anything"));
        store.Set("k", "v");

        Assert.Equal("v", NewStore().Get("k"));
        var quarantined = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + ".corrupt-*"));
        Assert.Equal("{ not json", File.ReadAllText(quarantined));
    }

    [Fact]
    public void Set_WhenFileIsLocked_ThrowsInsteadOfDroppingOtherSecrets()
    {
        var store = NewStore();
        store.Set("a", "1");

        // A fresh instance has nothing cached, so it must read the (locked) file before writing.
        var other = NewStore();
        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => other.Set("b", "2"));
        }

        Assert.Equal("1", NewStore().Get("a"));
    }

    [Fact]
    public void Get_UsesCachedMap_AfterFirstRead()
    {
        var store = NewStore();
        store.Set("k", "v");

        // The process is the only writer, so once loaded the store doesn't need the file.
        using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal("v", store.Get("k"));
        }
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + ".*"))
        {
            File.Delete(file);
        }
    }
}
