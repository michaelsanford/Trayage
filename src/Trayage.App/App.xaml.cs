using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.WinUI.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trayage.App.Logging;
using Trayage.App.Notifications;
using Trayage.App.Tray;
using Trayage.App.ViewModels;
using Trayage.App.Views;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;

namespace Trayage.App;

/// <summary>
/// Application entry point. Trayage runs as a tray-only app: there is no startup
/// window, so the WPF shutdown mode is explicit and the lifetime is driven by the
/// tray icon. A generic host provides DI and (later) background services.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Trayage.SingleInstance.6f1f3a2e";

    private Mutex? _singleInstanceMutex;
    private IHost? _host;
    private TrayIconService? _tray;
    private Window? _trayHost;

    /// <summary>True once the user has chosen Quit, so windows stop hiding and actually close.</summary>
    public static bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterGlobalExceptionHandlers();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // A toast click while the app is already running is routed to the live
            // instance via COM, so a second launch can exit silently.
            if (!ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
            {
                MessageBox.Show("Trayage is already running. Look for its icon in the system tray.",
                    "Trayage", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        // Open the page behind a toast when the user clicks it.
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;

        // Tray-only: never quit just because no window is open.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Anchor configuration to the executable's directory (not the launch CWD) so the
        // shipped appsettings.json is always found. appsettings.local.json — gitignored —
        // layers developer credentials on top for local runs.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

        // Verbosity comes from saved settings (read directly, before DI exists). Verbose mode
        // captures Debug-level detail for bug reports; logs never contain tokens or logins.
        var verbose = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance).Load().VerboseLogging;
        var logLevel = verbose ? LogLevel.Debug : LogLevel.Information;

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(logLevel);
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(TrayagePaths.LogDirectory, "trayage.log"), logLevel));
        builder.ConfigureTrayageServices();
        _host = builder.Build();

        await _host.StartAsync();

        var settingsStore = _host.Services.GetRequiredService<ISettingsStore>();
        var settings = settingsStore.Load();

        // Apply the saved theme before any window is shown.
        Services.ThemeApplier.Apply(settings.Theme);

        // WPF-UI's tray icon needs a window handle to hook onto. As a tray-only app we
        // have no real window, so create a hidden, off-screen host window to anchor it.
        _trayHost = CreateTrayHostWindow();
        MainWindow = _trayHost;

        _tray = _host.Services.GetRequiredService<TrayIconService>();
        WireTray(_tray);
        _tray.SetParentWindow(_trayHost);
        var registered = _tray.Register();
        _host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Trayage.App")
            .LogInformation("Tray icon registered: {Registered}", registered);

        // Reflect connection + unread state on the tray icon as the inbox changes.
        // Connect/disconnect both trigger a refresh, so this also catches sign-in/out.
        // The hosted InboxPollingService performs the first (and recurring) refreshes.
        var inboxState = _host.Services.GetRequiredService<InboxState>();
        inboxState.Changed += (_, _) => RefreshTrayStatus();
        RefreshTrayStatus();

        // On the very first launch, pop the inbox flyout so it's obvious Trayage started
        // and where the Settings button is. Shown only once.
        if (!settings.FirstRunCompleted)
        {
            Flyout.ShowNearTray();
            settings.FirstRunCompleted = true;
            settingsStore.Save(settings);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Unregister();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void WireTray(TrayIconService tray)
    {
        tray.QuitRequested += Quit;
        tray.LeftClicked += ToggleInboxFlyout;
        tray.OpenInboxRequested += ShowInboxFlyout;
        tray.RefreshRequested += RefreshInbox;
        tray.SettingsRequested += ShowSettings;

        var inboxViewModel = _host!.Services.GetRequiredService<InboxViewModel>();
        inboxViewModel.OpenSettingsRequested += ShowSettings;
    }

    private void Quit()
    {
        IsShuttingDown = true;
        Shutdown();
    }

    /// <summary>
    /// Creates a 1×1, off-screen, taskbar-less window purely to provide the tray icon a
    /// valid HWND and presentation source. The explicit empty style opts it out of
    /// WPF-UI's implicit Window style (which would otherwise try to toggle
    /// AllowsTransparency after the handle exists and throw). It is shown off-screen so
    /// the tray's PresentationSource lookup succeeds, but is visually imperceptible.
    /// </summary>
    private static Window CreateTrayHostWindow()
    {
        var host = new Window
        {
            Width = 1,
            Height = 1,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Style = new System.Windows.Style(typeof(Window)),
        };

        host.Show();
        return host;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("AppDomain", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("UnobservedTask", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Dispatcher", e.Exception);
        // Keep the tray app alive through non-fatal UI exceptions.
        e.Handled = true;
    }

    private InboxFlyout Flyout => _host!.Services.GetRequiredService<InboxFlyout>();

    private void ToggleInboxFlyout() => Flyout.Toggle();

    private void ShowInboxFlyout() => Flyout.ShowNearTray();

    private void RefreshInbox()
    {
        var inboxService = _host!.Services.GetRequiredService<InboxService>();
        _ = inboxService.RefreshAsync(CancellationToken.None);
    }

    /// <summary>
    /// Recomputes the tray icon state. Grey ("not connected") means no account has been
    /// set up at all; once at least one account is configured we follow its connection
    /// flow — amber when unread items wait, green when caught up — even if the live
    /// session is momentarily unavailable (e.g. before the first poll restores it).
    /// Connection state is persisted, so this is stable across the startup gap. May be
    /// called off the UI thread (e.g. from <see cref="InboxState.Changed"/>); the icon
    /// swap itself is marshalled to the UI thread.
    /// </summary>
    private void RefreshTrayStatus()
    {
        if (_tray is null || _host is null)
        {
            return;
        }

        var settings = _host.Services.GetRequiredService<ISettingsStore>().Load();
        var anyConfigured = settings.GitHub.Connected
            || settings.Bitbucket.Connected
            || _host.Services.GetServices<IInboxProvider>().Any(p => p.IsConnected);

        var inboxState = _host.Services.GetRequiredService<InboxState>();

        var status = !anyConfigured ? TrayStatus.Disconnected
            : inboxState.HasUnread ? TrayStatus.Unread
            : TrayStatus.CaughtUp;

        _tray.SetStatus(status, inboxState.UnreadCount);
    }

    private void ShowSettings() =>
        _host!.Services.GetRequiredService<SettingsWindow>().ShowAndActivate();

    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var arguments = ToastArguments.Parse(e.Argument);
        if (arguments.Contains(WindowsToastNotifier.UrlArgumentKey))
        {
            var url = arguments[WindowsToastNotifier.UrlArgumentKey];
            if (!string.IsNullOrEmpty(url))
            {
                InboxViewModel.OpenUrl(url);
            }
        }
    }
}
