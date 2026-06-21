using Trayage.App.ViewModels;
using Wpf.Ui.Controls;

namespace Trayage.App.Views;

/// <summary>The Fluent settings window. A single instance is reused and hidden on close.</summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Height tracks 60% of the screen's work area (rather than a fixed value) so the long
        // Settings panes get more room and scale with the monitor. Clamped to MinHeight and the
        // available height so it never shrinks below usable or overflows the screen.
        var workAreaHeight = System.Windows.SystemParameters.WorkArea.Height;
        Height = Math.Clamp(workAreaHeight * 0.6, MinHeight, workAreaHeight);
    }

    /// <summary>Brings the window to the front, restoring it if minimised or hidden.</summary>
    public void ShowAndActivate()
    {
        // Re-check toast availability in case the runtime was installed since last shown.
        (DataContext as SettingsViewModel)?.RefreshNotificationAvailability();

        Show();
        if (WindowState == System.Windows.WindowState.Minimized)
        {
            WindowState = System.Windows.WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // Lazily discover Bitbucket repositories the first time that tab is opened (while connected),
    // so the picker fills itself instead of needing an explicit "load" click. The result is cached
    // for the window's lifetime; the in-pane Refresh button reloads on demand.
    private void OnTabSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // TabControl.SelectionChanged also bubbles up from ComboBoxes inside the tabs; only act on
        // the TabControl's own selection change.
        if (e.OriginalSource is not System.Windows.Controls.TabControl tabControl)
        {
            return;
        }

        if (ReferenceEquals(tabControl.SelectedItem, BitbucketTab))
        {
            (DataContext as SettingsViewModel)?.EnsureBitbucketReposLoaded();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Keep the single instance alive for the app's lifetime; hide instead of close —
        // unless the app is actually quitting, in which case let it close.
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
