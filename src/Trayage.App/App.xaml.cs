using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trayage.App.Logging;
using Trayage.App.Notifications;
using Trayage.App.Tray;
using Trayage.App.ViewModels;
using Trayage.Core.Configuration;
using Trayage.Core.Providers;
using Trayage.Core.Security;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Trayage.App;

/// <summary>
/// Application entry point. Trayage runs as a tray-only app: there is no startup
/// window, so the WPF shutdown mode is explicit and the lifetime is driven by the
/// tray icon. A generic host provides DI and (later) background services.
/// </summary>
public partial class App
{
    private const string SingleInstanceMutexName = "Trayage.SingleInstance.6f1f3a2e";

    private Mutex? _singleInstanceMutex;
    private IHost? _host;
    private TrayIconService? _tray;
    private Window? _trayHost;
    private bool _notificationsRegistered;

    // Available from the moment the host is built. Defaults to a no-op so the session-end
    // path can log unconditionally even if Windows ends the session mid-startup.
    private ILogger _logger = NullLogger.Instance;

    /// <summary>True once the user has chosen Quit, so windows stop hiding and actually close.</summary>
    public static bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var onStartupMs = StartupClock.ElapsedMilliseconds();
        base.OnStartup(e);

        RegisterGlobalExceptionHandlers();

        _singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray. When the app is already running a toast click is
            // delivered in-process to that instance (see OnNotificationInvoked), so this second
            // process exits quietly; only a manual relaunch shows the reminder. It must NOT call
            // AppNotificationManager.Register(), which would steal the COM activator from the
            // running instance.
            if (AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind != ExtendedActivationKind.AppNotification)
            {
                MessageBox.Show("Trayage is already running. Look for its icon in the system tray.",
                    "Trayage", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        // Tray-only: never quit just because no window is open.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Show the tray icon before anything else, so a launch (especially a cold one at login)
        // gives immediate feedback. Only what the icon itself needs happens first: the saved
        // theme (one small JSON read, so the menu is styled correctly) and the hidden host
        // window. Everything else waits until the shell has painted the icon.
        var earlySettings = new JsonSettingsStore(NullLogger<JsonSettingsStore>.Instance).Load();
        Services.ThemeApplier.Apply(earlySettings.Theme);

        // WPF-UI's tray icon needs a window handle to hook onto. As a tray-only app we
        // have no real window, so create a hidden, off-screen host window to anchor it.
        _trayHost = CreateTrayHostWindow();
        MainWindow = _trayHost;

        // Starts in the faded "starting" state with only Quit enabled, so even a slow or
        // failed startup can be exited from the tray.
        _tray = new TrayIconService();
        _tray.QuitRequested += Quit;
        _tray.SetParentWindow(_trayHost);
        var registered = _tray.Register();
        var trayShownMs = StartupClock.ElapsedMilliseconds();

        // Let the shell paint the icon before the heavier startup work below.
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            await StartHostAsync(earlySettings.VerboseLogging);
            _logger.LogInformation(
                "Tray icon registered: {Registered} ({Elapsed} ms after process start; OnStartup began at {OnStartup} ms).",
                registered, trayShownMs, onStartupMs);
        }
        catch (Exception ex)
        {
            // Without this, the dispatcher's unhandled-exception handler would swallow the
            // failure and leave a process that holds the single-instance mutex but does nothing.
            CrashLog.Write("Startup", ex);
            _tray.SetStatus(TrayStatus.Failed);
            MessageBox.Show(
                $"Trayage couldn't start: {ex.Message}\n\nDetails were written to {TrayagePaths.LogDirectory}.",
                "Trayage", MessageBoxButton.OK, MessageBoxImage.Error);
            Quit();
        }
    }

    /// <summary>
    /// Everything that needs the generic host, run after the tray icon is already visible.
    /// </summary>
    private async Task StartHostAsync(bool verbose)
    {
        // Open the page behind a toast when the user clicks it. The handler must be registered
        // before Register() so a click arrives in-process instead of launching a new instance.
        // Guarded by IsSupported(): app notifications need the Windows App SDK Singleton package,
        // which isn't part of a self-contained deployment — light them up only when available.
        if (AppNotificationManager.IsSupported())
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;

            // If this instance was cold-launched by a toast click, open its page now.
            if (AppInstance.GetCurrent().GetActivatedEventArgs() is { Kind: ExtendedActivationKind.AppNotification } activation)
            {
                OpenToastUrl((AppNotificationActivatedEventArgs)activation.Data);
            }
        }

        // Building the host, migrating settings and restoring every account's session is file
        // I/O and DPAPI work that touches nothing in WPF (DI builds the UI singletons lazily, on
        // first resolve), so it runs off the UI thread to keep the tray menu responsive.
        var host = await Task.Run(() => BuildHost(verbose));
        if (IsShuttingDown)
        {
            // Quit was chosen from the tray while the host was being built.
            host.Dispose();
            return;
        }

        _host = host;
        _logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Trayage.App");

        // Before the host starts, so the first poll's snapshot can't be missed.
        var trayController = new TrayController(host.Services, _tray!, _logger);
        trayController.Attach();

        // The hosted InboxPollingService performs the first (and recurring) refreshes.
        await host.StartAsync();
        _logger.LogInformation("Host started ({Elapsed} ms after process start).", StartupClock.ElapsedMilliseconds());

        // On the very first launch, pop the inbox flyout so it's obvious Trayage started
        // and where the Settings button is. Shown only once.
        var settingsStore = host.Services.GetRequiredService<ISettingsStore>();
        var settings = settingsStore.Load();
        if (!settings.FirstRunCompleted)
        {
            trayController.ShowInbox();
            settings.FirstRunCompleted = true;
            settingsStore.Save(settings);
        }
    }

    private static IHost BuildHost(bool verbose)
    {
        // Anchor configuration to the executable's directory (not the launch CWD) so the
        // shipped appsettings.json is always found. appsettings.local.json — gitignored —
        // layers developer credentials on top for local runs.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

        // Verbose mode captures Debug-level detail for bug reports; logs never contain tokens or logins.
        var logLevel = verbose ? LogLevel.Debug : LogLevel.Information;
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(logLevel);
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(TrayagePaths.LogDirectory, "trayage.log"), logLevel));
        builder.ConfigureTrayageServices();
        var host = builder.Build();

        // Upgrade a pre-accounts settings.json (and re-key its tokens) *before* anything reads
        // accounts — the hosted poller resolves providers as soon as the host starts.
        var migrationLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Trayage.Migration");
        SettingsMigration.Run(
            host.Services.GetRequiredService<ISettingsStore>(),
            host.Services.GetRequiredService<ISecretStore>(),
            migrationLogger);

        // Build one provider instance per configured account.
        host.Services.GetRequiredService<ProviderRegistry>().Initialize();
        return host;
    }

    /// <summary>
    /// Windows is logging off, shutting down or restarting. The OS allows roughly five seconds
    /// to answer WM_QUERYENDSESSION and exit before it shows the full-screen "this app is
    /// preventing shutdown" page — and Trayage qualifies for that page because the off-screen
    /// tray host counts as a visible top-level window. WPF's default path would fall through to
    /// <see cref="OnExit"/>, which awaits the host stop and unregisters the toast COM activator;
    /// either can outlast the budget. So session-end teardown is deliberately minimal: drop the
    /// tray icon and terminate. Nothing is lost — settings and tokens are written atomically as
    /// they change, and the file logger is unbuffered.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        BeginShutdown();

        try
        {
            _logger.LogInformation("Session ending ({Reason}); exiting immediately.", e.ReasonSessionEnding);
            _tray?.Unregister();
            _logger.LogInformation("Tray icon removed for session end.");
            base.OnSessionEnding(e);
            _logger.LogInformation("Session end teardown complete; terminating.");
        }
        catch (Exception ex)
        {
            // Never let cleanup trouble keep the process alive past the OS budget.
            CrashLog.Write("SessionEnding", ex);
        }
        finally
        {
            // Deterministic and immediate, unlike WPF's dispatcher-queued Shutdown().
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// The user-initiated teardown (tray ▸ Quit). Windows shutdown does not come through here —
    /// see <see cref="OnSessionEnding"/> — so this path can afford to be graceful.
    /// </summary>
    /// <remarks>
    /// Deliberately synchronous. WPF tears the process down as soon as this returns, so an
    /// <c>async void</c> override's continuation after its first await — disposing the host and
    /// the mutex — never ran. The host is stopped on the thread pool and waited for with a
    /// deadline: blocking the UI thread on it directly could deadlock against anything the
    /// stopping services marshal to the dispatcher.
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        // Only the primary instance registered, so only it unregisters — a second instance
        // calling Unregister() would tear down the running instance's COM activator.
        if (_notificationsRegistered)
        {
            AppNotificationManager.Default.Unregister();
        }

        _tray?.Unregister();

        if (_host is { } host)
        {
            try
            {
                // InboxPollingService honours its stopping token on both the poll and the
                // delay, so it stops promptly; a longer timeout would only mask a hang.
                var stopped = Task.Run(() => host.StopAsync(TimeSpan.FromSeconds(2))).Wait(TimeSpan.FromSeconds(3));
                _logger.LogInformation(stopped ? "Host stopped." : "Host didn't stop within 3 s; exiting anyway.");
            }
            catch (Exception ex)
            {
                CrashLog.Write("HostStop", ex);
            }

            host.Dispose();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void Quit()
    {
        BeginShutdown();
        Shutdown();
    }

    /// <summary>
    /// Marks the app as quitting so windows that normally hide on close (Settings) actually
    /// close. Shared by the tray Quit menu item and by Windows session end.
    /// </summary>
    private static void BeginShutdown() => IsShuttingDown = true;

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
            Style = new Style(typeof(Window)),
        };

        // Create the HWND first so it can be marked as a tool window (excluded from the
        // Alt-Tab switcher) before it's shown; ShowInTaskbar=false alone doesn't do that.
        _ = new System.Windows.Interop.WindowInteropHelper(host).EnsureHandle();
        Interop.NativeWindow.HideFromAltTab(host);

        host.Show();
        return host;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write("AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
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

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        => OpenToastUrl(args);

    private static void OpenToastUrl(AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue(WindowsToastNotifier.UrlArgumentKey, out var url) && !string.IsNullOrEmpty(url))
        {
            InboxViewModel.OpenUrl(url);
        }
    }

}
