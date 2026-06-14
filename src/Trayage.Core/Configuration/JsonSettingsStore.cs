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

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? TrayagePaths.SettingsFile;
    }

    public string FilePath => _filePath;

    public TrayageSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new TrayageSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<TrayageSettings>(json, SerializerOptions) ?? new TrayageSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Failed to read settings from {Path}; falling back to defaults.", _filePath);
            return new TrayageSettings();
        }
    }

    public void Save(TrayageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

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
    }
}
