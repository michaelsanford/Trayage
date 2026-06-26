namespace Trayage.Core.Configuration;

/// <summary>
/// Resolves Trayage's per-user data locations under %APPDATA%\Trayage. Centralised so
/// settings, the secret store, and logs all agree on where state lives.
/// </summary>
public static class TrayagePaths
{
    private const string AppFolderName = "Trayage";

    /// <summary>%APPDATA%\Trayage, created on first access.</summary>
    private static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string SecretsFile => Path.Combine(DataDirectory, "secrets.dat");

    public static string LogDirectory
    {
        get
        {
            var dir = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
