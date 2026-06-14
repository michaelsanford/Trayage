namespace Trayage.Core.Security;

/// <summary>
/// A small key/value vault for sensitive strings (OAuth access &amp; refresh tokens).
/// Implementations are responsible for protecting values at rest.
/// </summary>
public interface ISecretStore
{
    /// <summary>Stores (or overwrites) the secret for <paramref name="key"/>.</summary>
    void Set(string key, string value);

    /// <summary>Returns the secret, or null if absent or it could not be decrypted.</summary>
    string? Get(string key);

    void Remove(string key);

    bool Contains(string key);
}
