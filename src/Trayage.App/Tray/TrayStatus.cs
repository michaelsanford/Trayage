namespace Trayage.App.Tray;

/// <summary>
/// The overall state the tray icon reflects, shown as a tint on the inbox-tray glyph
/// plus a symbol rising above it: a blue tray when connected and caught up, a rising sun
/// when unread items are waiting, and a grey tray with a symbol when no provider is
/// connected. The disconnected state distinguishes "nothing configured" (a "?") from
/// "configured but no live session" (a red "X") via the <c>connectionError</c> flag on
/// <see cref="TrayIconService.SetStatus"/>.
/// </summary>
public enum TrayStatus
{
    /// <summary>No provider holds a live, authenticated session.</summary>
    Disconnected,

    /// <summary>Connected, with no unread items.</summary>
    CaughtUp,

    /// <summary>Connected, with unread items awaiting action.</summary>
    Unread,
}
