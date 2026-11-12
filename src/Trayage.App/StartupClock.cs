using System.Diagnostics;

namespace Trayage.App;

/// <summary>Measures startup milestones from the moment the OS created the process.</summary>
internal static class StartupClock
{
    public static long ElapsedMilliseconds()
    {
        using var process = Process.GetCurrentProcess();
        return (long)(DateTime.Now - process.StartTime).TotalMilliseconds;
    }
}
