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
    private readonly System.Threading.Lock _gate = new();

    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            var map = Read();
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
            var map = Read();
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
            var map = Read();
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
            return Read().ContainsKey(key);
        }
    }

    private Dictionary<string, string> Read()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Secrets file at {Path} was unreadable; starting empty.", _filePath);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void Write(Dictionary<string, string> map)
    {
        var json = JsonSerializer.Serialize(map);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }
    }
}
