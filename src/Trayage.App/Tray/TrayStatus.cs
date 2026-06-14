namespace Trayage.App.Tray;

/// <summary>
/// The overall state the tray icon reflects through colour: grey when no provider is
/// connected, amber when unread items are waiting, green when connected and caught up.
/// </summary>
public enum TrayStatus
{
    /// <summary>No provider is signed in.</summary>
    Disconnected,

    /// <summary>Connected, with no unread items.</summary>
    CaughtUp,

    /// <summary>Connected, with unread items awaiting action.</summary>
    Unread,
}
