using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Trayage.App.ViewModels;

namespace Trayage.App.Views;

/// <summary>The Fluent settings window. A single instance is reused and hidden on close.</summary>
public partial class SettingsWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Height tracks 60% of the screen's work area (rather than a fixed value) so the long
        // Settings panes get more room and scale with the monitor. Clamped to MinHeight and the
        // available height so it never shrinks below usable or overflows the screen.
        var workAreaHeight = SystemParameters.WorkArea.Height;
        Height = Math.Clamp(workAreaHeight * 0.6, MinHeight, workAreaHeight);
    }

    /// <summary>The panes, in the same order as the navigation rail's items.</summary>
    private UIElement[] Panes => field ??= new UIElement[]
    {
        AccountsPane, NotificationsPane, InboxPane, GeneralPane, AboutPane,
    };

    /// <summary>Brings the window to the front, restoring it if minimised or hidden.</summary>
    public void ShowAndActivate()
    {
        // Re-check toast availability in case the runtime was installed since last shown.
        (DataContext as SettingsViewModel)?.RefreshNotificationAvailability();

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Shows the pane matching the rail's selection and hides the rest.</summary>
    private void OnNavigationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // ComboBoxes inside the panes also raise SelectionChanged, which bubbles; only act on
        // the rail's own selection change.
        if (!ReferenceEquals(e.OriginalSource, Nav))
        {
            return;
        }

        var selected = Nav.SelectedIndex;
        for (var i = 0; i < Panes.Length; i++)
        {
            Panes[i].Visibility = i == selected ? Visibility.Visible : Visibility.Collapsed;
        }

        // Entering Accounts kicks off Bitbucket repository discovery so each account's picker is
        // populated by the time its card is expanded.
        if (selected == 0)
        {
            (DataContext as SettingsViewModel)?.EnsureAccountReposLoaded();
        }
    }

    /// <summary>
    /// Loads an account's repositories the first time its card is opened. Cheap to call again —
    /// the view-model only fetches once per account.
    /// </summary>
    private void OnAccountExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProviderAccountViewModel account })
        {
            account.EnsureReposLoaded();
        }
    }

    /// <summary>
    /// Commits the volume when the slider thumb is released. Persisting on every value change
    /// would rewrite settings.json — and replay the preview sound — for each pixel of travel.
    /// </summary>
    private void OnVolumeDragCompleted(object sender, DragCompletedEventArgs e) =>
        (DataContext as SettingsViewModel)?.CommitVolume();

    /// <summary>
    /// Commits a volume change that didn't come from a drag — a click on the track, or the
    /// arrow keys — which produce no DragCompleted.
    /// </summary>
    private void OnVolumeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider { IsMouseCaptureWithin: false })
        {
            (DataContext as SettingsViewModel)?.CommitVolume();
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
