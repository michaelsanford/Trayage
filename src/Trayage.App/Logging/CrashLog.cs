using System.IO;
using Trayage.Core.Configuration;

namespace Trayage.App.Logging;

/// <summary>
/// Last-resort logger for unhandled exceptions, used before the host's logger exists or
/// when the app is already failing. Best-effort and never throws.
/// </summary>
public static class CrashLog
{
    public static string FilePath => Path.Combine(TrayagePaths.LogDirectory, "crash.log");

    public static void Write(string context, Exception? exception)
    {
        try
        {
            var entry = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{context}] {exception}{Environment.NewLine}";
            File.AppendAllText(FilePath, entry);
        }
        catch
        {
            // Swallow — there's nowhere left to report to.
        }
    }
}
