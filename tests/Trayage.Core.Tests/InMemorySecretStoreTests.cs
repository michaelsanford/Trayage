using Trayage.Core.Security;

namespace Trayage.Core.Tests;

public sealed class InMemorySecretStoreTests
{
    [Fact]
    public void Store_PerformsCrudOperationsCorrectly()
    {
        var store = new InMemorySecretStore();
        
        Assert.False(store.Contains("key"));
        Assert.Null(store.Get("key"));

        store.Set("key", "secret");
        Assert.True(store.Contains("key"));
        Assert.Equal("secret", store.Get("key"));

        store.Set("key", "new-secret");
        Assert.Equal("new-secret", store.Get("key"));

        store.Remove("key");
        Assert.False(store.Contains("key"));
        Assert.Null(store.Get("key"));
    }
}
