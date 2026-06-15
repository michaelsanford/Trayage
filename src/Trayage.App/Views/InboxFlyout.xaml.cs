using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Trayage.App.ViewModels;

namespace Trayage.App.Views;

/// <summary>
/// Borderless popup anchored at the bottom-right of the work area, shown from the tray.
/// It hides (rather than closes) when it loses focus or is toggled, so the single
/// instance lives for the app's lifetime.
/// </summary>
public partial class InboxFlyout : Window
{
    private static readonly TimeSpan ShowGuard = TimeSpan.FromMilliseconds(300);

    private DateTime _shownAtUtc;
    private DateTime _hiddenAtUtc;

    public InboxFlyout(InboxViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Deactivated += OnDeactivated;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Keep the borderless flyout out of the Alt-Tab switcher.
        Interop.NativeWindow.HideFromAltTab(this);
    }

    /// <summary>Shows the flyout near the tray, or hides it if already visible.</summary>
    public void Toggle()
    {
        // If a tray click just auto-hid us, that same click shouldn't immediately reopen.
        if (!IsVisible && DateTime.UtcNow - _hiddenAtUtc < ShowGuard)
        {
            return;
        }

        if (IsVisible)
        {
            HideFlyout();
        }
        else
        {
            ShowNearTray();
        }
    }

    /// <summary>Positions the flyout near the tray and brings it to the foreground.</summary>
    public void ShowNearTray()
    {
        const double margin = 12;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - margin;
        Top = workArea.Bottom - Height - margin;

        _shownAtUtc = DateTime.UtcNow;
        Show();
        ForceForeground();
    }

    private void HideFlyout()
    {
        _hiddenAtUtc = DateTime.UtcNow;
        Hide();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Ignore the spurious deactivation that fires during the show transition when
        // invoked from a tray click; genuine click-away dismissals arrive later.
        if (DateTime.UtcNow - _shownAtUtc < ShowGuard)
        {
            return;
        }

        HideFlyout();
    }

    /// <summary>
    /// Brings the window to the foreground. A process triggered from a tray click isn't
    /// the foreground process, so a plain <see cref="Window.Activate"/> is unreliable;
    /// briefly attaching to the foreground thread's input lets SetForegroundWindow stick.
    /// </summary>
    private void ForceForeground()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var appThread = GetCurrentThreadId();

        if (foregroundThread != appThread && foreground != IntPtr.Zero)
        {
            AttachThreadInput(foregroundThread, appThread, true);
            SetForegroundWindow(handle);
            AttachThreadInput(foregroundThread, appThread, false);
        }
        else
        {
            SetForegroundWindow(handle);
        }

        Activate();
        Focus();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
