using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Tray;

namespace Trayage.App.Tray;

/// <summary>
/// Owns the Windows notification-area (system tray) icon and its context menu.
/// Raises events for menu actions so the host can wire them to application behaviour.
/// </summary>
public sealed class TrayIconService : NotifyIconService
{
    // WPF-UI's NotifyIconService.Icon/TooltipText are plain auto-properties: setting them
    // after Register() does NOT update the live shell icon. The refresh methods
    // (ModifyIcon/ModifyToolTip, which call Shell_NotifyIcon NIM_MODIFY) live on the
    // internal manager and aren't exposed, so we reach them by reflection. Re-registering
    // instead would leak hook windows and bump the icon id on every change.
    private static readonly FieldInfo? ManagerField =
        typeof(NotifyIconService).GetField("internalNotifyIconManager", BindingFlags.NonPublic | BindingFlags.Instance);

    private MethodInfo? _modifyIcon;
    private MethodInfo? _modifyToolTip;
    private static readonly Uri DisconnectedIconUri = new("pack://application:,,,/Assets/trayage-disconnected.ico", UriKind.Absolute);
    private static readonly Uri CaughtUpIconUri = new("pack://application:,,,/Assets/trayage-caughtup.ico", UriKind.Absolute);
    private static readonly Uri UnreadIconUri = new("pack://application:,,,/Assets/trayage-unread.ico", UriKind.Absolute);

    private readonly ImageSource _disconnectedIcon = new BitmapImage(DisconnectedIconUri);
    private readonly ImageSource _caughtUpIcon = new BitmapImage(CaughtUpIconUri);
    private readonly ImageSource _unreadIcon = new BitmapImage(UnreadIconUri);

    public event Action? LeftClicked;
    public event Action? OpenInboxRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIconService()
    {
        TooltipText = "Trayage — not connected";
        Icon = _disconnectedIcon;
        ContextMenu = BuildContextMenu();
    }

    /// <summary>
    /// Swaps the tray icon (and tooltip) to reflect the current connection and unread
    /// state. <paramref name="unreadCount"/> is only used to enrich the tooltip text.
    /// </summary>
    public void SetStatus(TrayStatus status, int unreadCount = 0)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            Icon = status switch
            {
                TrayStatus.Unread => _unreadIcon,
                TrayStatus.CaughtUp => _caughtUpIcon,
                _ => _disconnectedIcon,
            };

            TooltipText = status switch
            {
                TrayStatus.Unread => unreadCount > 0 ? $"Trayage — {unreadCount} waiting" : "Trayage — items waiting",
                TrayStatus.CaughtUp => "Trayage — all caught up",
                _ => "Trayage — not connected",
            };

            PushToShell();
        });
    }

    /// <summary>
    /// Pushes the current <see cref="NotifyIconService.Icon"/> and
    /// <see cref="NotifyIconService.TooltipText"/> to the live shell icon. A no-op until
    /// the icon is registered (Register() reads the initial values) and degrades to a
    /// no-op if WPF-UI's internals ever change shape.
    /// </summary>
    private void PushToShell()
    {
        if (!IsRegistered || ManagerField?.GetValue(this) is not { } manager)
        {
            return;
        }

        var managerType = manager.GetType();
        _modifyIcon ??= managerType.GetMethod("ModifyIcon", Type.EmptyTypes);
        _modifyToolTip ??= managerType.GetMethod("ModifyToolTip", Type.EmptyTypes);
        _ = _modifyIcon?.Invoke(manager, null);
        _ = _modifyToolTip?.Invoke(manager, null);
    }

    protected override void OnLeftClick() => LeftClicked?.Invoke();

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("Open inbox", () => OpenInboxRequested?.Invoke()));
        menu.Items.Add(CreateItem("Refresh now", () => RefreshRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Settings…", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Quit Trayage", () => QuitRequested?.Invoke()));
        return menu;
    }

    private static MenuItem CreateItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }
}
