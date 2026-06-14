namespace Trayage.Core.Configuration;

/// <summary>Loads and persists <see cref="TrayageSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Absolute path of the backing file (shown in Settings / used in logs).</summary>
    string FilePath { get; }

    /// <summary>Returns saved settings, or a fresh default instance if none exist or parsing fails.</summary>
    TrayageSettings Load();

    void Save(TrayageSettings settings);
}
