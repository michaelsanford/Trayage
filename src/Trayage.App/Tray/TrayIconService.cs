using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Tray;
#if DEBUG
using Trayage.Core.Models;
#endif

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
    private static readonly Uri ErrorIconUri = new("pack://application:,,,/Assets/trayage-error.ico", UriKind.Absolute);
    private static readonly Uri StartingIconUri = new("pack://application:,,,/Assets/trayage-starting.ico", UriKind.Absolute);

    private readonly ImageSource _disconnectedIcon = LoadIcon(DisconnectedIconUri);
    private readonly ImageSource _caughtUpIcon = LoadIcon(CaughtUpIconUri);
    private readonly ImageSource _unreadIcon = LoadIcon(UnreadIconUri);
    private readonly ImageSource _errorIcon = LoadIcon(ErrorIconUri);
    private readonly ImageSource _startingIcon = LoadIcon(StartingIconUri);

    // Menu entries that need the host (inbox, refresh, settings); disabled until SetReady(true).
    private readonly List<MenuItem> _hostDependentItems = [];
    private bool _ready;

    public event Action? LeftClicked;
    public event Action? OpenInboxRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
#if DEBUG
    /// <summary>Debug-only developer aid: injects a synthetic item of the given provider/kind into the live stream.</summary>
    public event Action<ProviderKind, InboxItemKind>? InjectRequested;
#endif

    /// <summary>
    /// Created before the generic host exists, so the icon can be shown the moment the process
    /// starts: it opens in the faded <see cref="TrayStatus.Starting"/> state with only Quit
    /// enabled, and the app calls <see cref="SetReady"/> once the host is up.
    /// </summary>
    public TrayIconService()
    {
        TooltipText = TooltipFor(TrayStatus.Starting, 0, false);
        Icon = _startingIcon;
        ContextMenu = BuildContextMenu();
        SetReady(false);
    }

    /// <summary>
    /// Enables the menu entries and left-click that need the host (inbox, refresh, settings).
    /// Quit is always available, so a slow or failed startup can still be exited from the tray.
    /// Must be called on the UI thread.
    /// </summary>
    public void SetReady(bool ready)
    {
        _ready = ready;
        foreach (var item in _hostDependentItems)
        {
            item.IsEnabled = ready;
        }
    }

    /// <summary>
    /// Swaps the tray icon (and tooltip) to reflect the current connection and unread
    /// state. <paramref name="unreadCount"/> is only used to enrich the tooltip text.
    /// <paramref name="connectionError"/> is consulted only when <paramref name="status"/>
    /// is <see cref="TrayStatus.Disconnected"/>: when true the icon shows a red "X"
    /// (a provider is configured but has no live session); when false it shows a "?"
    /// (nothing is configured).
    /// </summary>
    public void SetStatus(TrayStatus status, int unreadCount = 0, bool connectionError = false)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        // Called from the poller's thread on every inbox change: post rather than block it on
        // a UI round-trip. On the UI thread already, apply inline so the order is preserved.
        if (dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ = dispatcher.InvokeAsync(Apply);
        }

        void Apply()
        {
            var icon = (status, connectionError) switch
            {
                (TrayStatus.Starting or TrayStatus.Syncing, _) => _startingIcon,
                (TrayStatus.Unread, _) => _unreadIcon,
                (TrayStatus.CaughtUp, _) => _caughtUpIcon,
                (TrayStatus.Disconnected, true) or (TrayStatus.Failed, _) => _errorIcon,
                _ => _disconnectedIcon,
            };
            var tooltip = TooltipFor(status, unreadCount, connectionError);

            // Every poll lands here; skip the two reflective NIM_MODIFY calls when nothing changed.
            if (ReferenceEquals(icon, Icon) && tooltip == TooltipText)
            {
                return;
            }

            Icon = icon;
            TooltipText = tooltip;
            PushToShell();
        }
    }

    private static string TooltipFor(TrayStatus status, int unreadCount, bool connectionError) => (status, connectionError) switch
    {
        (TrayStatus.Starting, _) => "Trayage — starting…",
        (TrayStatus.Syncing, _) => "Trayage — syncing…",
        (TrayStatus.Unread, _) => unreadCount > 0 ? $"Trayage — {unreadCount} waiting" : "Trayage — items waiting",
        (TrayStatus.CaughtUp, _) => "Trayage — all caught up",
        (TrayStatus.Disconnected, true) => "Trayage — connection problem",
        (TrayStatus.Failed, _) => "Trayage — failed to start",
        _ => "Trayage — not connected",
    };

    private static BitmapImage LoadIcon(Uri uri)
    {
        var image = new BitmapImage(uri);
        image.Freeze();
        return image;
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

    protected override void OnLeftClick()
    {
        if (_ready)
        {
            LeftClicked?.Invoke();
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(HostDependent(CreateItem("Open inbox", () => OpenInboxRequested?.Invoke())));
        menu.Items.Add(HostDependent(CreateItem("Refresh now", () => RefreshRequested?.Invoke())));
        menu.Items.Add(new Separator());
        menu.Items.Add(HostDependent(CreateItem("Settings…", () => SettingsRequested?.Invoke())));
        menu.Items.Add(new Separator());
#if DEBUG
        // Debug-only developer aid (compiled out of Release/shipping builds): inject a
        // synthetic item for a given provider/kind to exercise the notification pipeline.
        menu.Items.Add(HostDependent(BuildInjectMenu()));
        menu.Items.Add(new Separator());
#endif
        menu.Items.Add(CreateItem("Quit Trayage", () => QuitRequested?.Invoke()));
        return menu;
    }

#if DEBUG
    // Debug-only developer aid: an "Inject ▸ <provider> ▸ <kind>" menu that pushes a synthetic
    // item into the live stream so its end-to-end handling can be observed. Each provider lists
    // only the kinds it can actually surface (see the GitHub and Bitbucket providers).
    private MenuItem BuildInjectMenu()
    {
        var inject = new MenuItem { Header = "Inject" };

        inject.Items.Add(BuildProviderMenu(ProviderKind.GitHub, new[]
        {
            InboxItemKind.ReviewRequest, InboxItemKind.Mention, InboxItemKind.Assignment,
            InboxItemKind.CiStatus, InboxItemKind.RepoActivity,
        }));

        // Bitbucket has no mention or CI/check feed, so those kinds never originate there.
        inject.Items.Add(BuildProviderMenu(ProviderKind.Bitbucket, new[]
        {
            InboxItemKind.ReviewRequest, InboxItemKind.Assignment, InboxItemKind.RepoActivity,
        }));

        // GitLab to-dos cover every kind via their action_name (assigned, review/approval,
        // mention, build_failed, plus general participation).
        inject.Items.Add(BuildProviderMenu(ProviderKind.GitLab, new[]
        {
            InboxItemKind.ReviewRequest, InboxItemKind.Mention, InboxItemKind.Assignment,
            InboxItemKind.CiStatus, InboxItemKind.Participating,
        }));

        return inject;
    }

    private MenuItem BuildProviderMenu(ProviderKind provider, InboxItemKind[] kinds)
    {
        var menu = new MenuItem { Header = provider.ToString() };
        foreach (var kind in kinds)
        {
            var capturedProvider = provider;
            var capturedKind = kind;
            menu.Items.Add(CreateItem(capturedKind.ToString(), () => InjectRequested?.Invoke(capturedProvider, capturedKind)));
        }

        return menu;
    }
#endif

    private MenuItem HostDependent(MenuItem item)
    {
        _hostDependentItems.Add(item);
        return item;
    }

    private static MenuItem CreateItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }
}
