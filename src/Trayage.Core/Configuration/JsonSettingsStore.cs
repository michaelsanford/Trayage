using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Trayage.Core.Configuration;

/// <summary>
/// Stores settings as human-readable JSON at <see cref="TrayagePaths.SettingsFile"/>.
/// Writes are atomic (temp file + replace) so a crash mid-save can't corrupt config.
/// </summary>
public sealed class JsonSettingsStore(ILogger<JsonSettingsStore> logger, string? filePath = null) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath = filePath ?? TrayagePaths.SettingsFile;
    private readonly System.Threading.Lock _gate = new();

    private TrayageSettings? _cache;
    private DateTime _cacheStampUtc;

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
                logger.LogWarning(ex, "Failed to read settings from {Path}; falling back to defaults.", _filePath);
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
