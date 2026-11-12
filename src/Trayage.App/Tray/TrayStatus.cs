namespace Trayage.App.Tray;

/// <summary>
/// The overall state the tray icon reflects, shown as the tint of the priority-bars glyph
/// (and whether its top bar is accented): blue with an amber top bar when unread items are
/// waiting, plain blue when connected and caught up, grey when no provider is connected, and faded blue while starting up.
/// The disconnected state distinguishes "nothing configured" (grey) from "configured but no
/// live session" (red) via the <c>connectionError</c> flag on
/// <see cref="TrayIconService.SetStatus"/>.
/// </summary>
public enum TrayStatus
{
    /// <summary>The process has just launched and the host isn't up yet (faded bars).</summary>
    Starting,

    /// <summary>The host is up and the first poll is in flight (faded bars).</summary>
    Syncing,

    /// <summary>No provider holds a live, authenticated session.</summary>
    Disconnected,

    /// <summary>Connected, with no unread items.</summary>
    CaughtUp,

    /// <summary>Connected, with unread items awaiting action.</summary>
    Unread,

    /// <summary>Startup failed; only Quit is available (red bars).</summary>
    Failed,
}
