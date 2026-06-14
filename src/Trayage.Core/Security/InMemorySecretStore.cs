using System.Collections.Concurrent;

namespace Trayage.Core.Security;

/// <summary>Non-persistent secret store for tests and design-time use.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public void Set(string key, string value) => _values[key] = value;

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Remove(string key) => _values.TryRemove(key, out _);

    public bool Contains(string key) => _values.ContainsKey(key);
}
