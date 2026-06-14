using System.Diagnostics;
using Microsoft.Win32;

namespace Trayage.App.Services;

/// <summary>
/// Toggles "start with Windows" by writing to the per-user Run key. No elevation needed,
/// and it's removed cleanly when disabled.
/// </summary>
public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Trayage";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string ExecutablePath =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? string.Empty;
}
