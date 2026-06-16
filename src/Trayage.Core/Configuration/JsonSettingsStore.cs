using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Trayage.Core.Configuration;

/// <summary>
/// Stores settings as human-readable JSON at <see cref="TrayagePaths.SettingsFile"/>.
/// Writes are atomic (temp file + replace) so a crash mid-save can't corrupt config.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly string _filePath;
    private readonly object _gate = new();

    // Cached parse of the settings file, keyed on its last-write time. Load() is called
    // several times per poll cycle (and on every inbox state change), so this avoids
    // re-reading and re-deserialising the file each time while still picking up external
    // edits (a cheap metadata stat invalidates the cache when the timestamp moves).
    private TrayageSettings? _cache;
    private DateTime _cacheStampUtc;

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? TrayagePaths.SettingsFile;
    }

    public string FilePath => _filePath;

    public TrayageSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _cache = null;
                return new TrayageSettings();
            }

            var stampUtc = File.GetLastWriteTimeUtc(_filePath);
            if (_cache is not null && stampUtc == _cacheStampUtc)
            {
                return _cache.Clone();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _cache = JsonSerializer.Deserialize<TrayageSettings>(json, SerializerOptions) ?? new TrayageSettings();
                _cacheStampUtc = stampUtc;
                return _cache.Clone();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.LogWarning(ex, "Failed to read settings from {Path}; falling back to defaults.", _filePath);
                _cache = null;
                return new TrayageSettings();
            }
        }
    }

    public void Save(TrayageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // File.Replace requires the destination to exist; fall back to Move on first save.
            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }

            // Refresh the cache from what we just wrote so the next Load() is a hit.
            _cache = settings.Clone();
            _cacheStampUtc = File.GetLastWriteTimeUtc(_filePath);
        }
    }
}
