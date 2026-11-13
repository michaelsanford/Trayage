using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trayage.Core.Configuration;

namespace Trayage.Core.Security;

/// <summary>
/// Secret store that encrypts each value with Windows DPAPI (CurrentUser scope) and
/// persists the ciphertext, base64-encoded, in a JSON map at
/// <see cref="TrayagePaths.SecretsFile"/>. Values are readable only by the same Windows
/// user on the same machine; the file never contains plaintext tokens.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(ILogger<DpapiSecretStore> logger, string? filePath = null) : ISecretStore
{
    // Extra entropy mixed into DPAPI so the blobs aren't decryptable by unrelated apps
    // that merely run as the same user.
    private static readonly byte[] Entropy = "Trayage.SecretStore.v1"u8.ToArray();

    private readonly string _filePath = filePath ?? TrayagePaths.SecretsFile;
    private readonly Lock _gate = new();

    private Dictionary<string, string>? _map;

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            // Mutate a copy so a failed write leaves the cache matching what's on disk.
            var map = new Dictionary<string, string>(Read(), StringComparer.Ordinal);
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
            map[key] = Convert.ToBase64String(cipher);
            Write(map);
        }
    }

    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_gate)
        {
            var map = TryRead();
            if (!map.TryGetValue(key, out var base64))
            {
                return null;
            }

            try
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(base64), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                logger.LogWarning(ex, "Could not decrypt secret {Key}; treating as absent.", key);
                return null;
            }
        }
    }

    public void Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_gate)
        {
            var map = new Dictionary<string, string>(Read(), StringComparer.Ordinal);
            if (map.Remove(key))
            {
                Write(map);
            }
        }
    }

    public bool Contains(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        lock (_gate)
        {
            return TryRead().ContainsKey(key);
        }
    }

    /// <summary>
    /// The key → ciphertext map (values are decrypted on demand), read once and cached: this process
    /// is the only writer, and token refreshes and session restores call in here often.
    /// </summary>
    private Dictionary<string, string> Read()
    {
        if (_map is not null)
        {
            return _map;
        }

        if (!File.Exists(_filePath))
        {
            return _map = [with(StringComparer.Ordinal)];
        }

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient (locked by AV, a sync client…): don't cache, and let the caller fail.
            // Returning an empty map here would let the next Set() overwrite every other
            // account's tokens with a one-entry file.
            logger.LogWarning(ex, "Secrets file at {Path} could not be read.", _filePath);
            throw;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return _map = loaded is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(loaded, StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            // Genuinely corrupt: keep the bytes for diagnosis, then start empty. Accounts will
            // need to reconnect, but a later Set() can no longer destroy anything recoverable.
            var quarantine = $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            logger.LogWarning(ex, "Secrets file at {Path} was corrupt; moved it to {Quarantine} and starting empty.", _filePath, quarantine);
            try
            {
                File.Move(_filePath, quarantine, overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(moveEx, "Could not move the corrupt secrets file aside.");
                throw;
            }

            return _map = [with(StringComparer.Ordinal)];
        }
    }

    private void Write(Dictionary<string, string> map)
    {
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(map));
        _map = map;
    }

    /// <summary>For the read-only paths: an unreadable file means "no secret", not a crash.</summary>
    private Dictionary<string, string> TryRead()
    {
        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [with(StringComparer.Ordinal)];
        }
    }
}
