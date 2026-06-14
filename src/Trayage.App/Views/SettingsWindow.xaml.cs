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
    }

    /// <summary>Brings the window to the front, restoring it if minimised or hidden.</summary>
    public void ShowAndActivate()
    {
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
