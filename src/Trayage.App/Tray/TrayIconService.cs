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
    private static readonly Uri NormalIconUri = new("pack://application:,,,/Assets/trayage.ico", UriKind.Absolute);
    private static readonly Uri UnreadIconUri = new("pack://application:,,,/Assets/trayage-unread.ico", UriKind.Absolute);

    private readonly ImageSource _normalIcon = new BitmapImage(NormalIconUri);
    private readonly ImageSource _unreadIcon = new BitmapImage(UnreadIconUri);

    public event Action? LeftClicked;
    public event Action? OpenInboxRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayIconService()
    {
        TooltipText = "Trayage";
        Icon = _normalIcon;
        ContextMenu = BuildContextMenu();
    }

    /// <summary>Swaps the tray icon to reflect whether action-needed items are waiting.</summary>
    public void SetUnread(bool hasUnread)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() => Icon = hasUnread ? _unreadIcon : _normalIcon);
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
