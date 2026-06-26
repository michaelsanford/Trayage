using Trayage.Core.Configuration;

namespace Trayage.Core.Tests;

public sealed class TrayagePathsTests
{
    [Fact]
    public void Paths_ContainExpectedFolderAndFileNames()
    {
        var settingsFile = TrayagePaths.SettingsFile;
        var secretsFile = TrayagePaths.SecretsFile;
        var logDir = TrayagePaths.LogDirectory;

        Assert.Contains("Trayage", settingsFile);
        Assert.EndsWith("settings.json", settingsFile);

        Assert.Contains("Trayage", secretsFile);
        Assert.EndsWith("secrets.dat", secretsFile);

        Assert.Contains("Trayage", logDir);
        Assert.EndsWith("logs", logDir);
    }

    [Fact]
    public void LogDirectory_ExistsAfterAccess()
    {
        var logDir = TrayagePaths.LogDirectory;
        Assert.True(Directory.Exists(logDir));
    }
}
