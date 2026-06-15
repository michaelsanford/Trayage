using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Trayage.App.Interop;

/// <summary>
/// Small native-window helpers. Trayage is a tray-only app, so its hidden host window and
/// the borderless flyout shouldn't appear in the Alt-Tab switcher.
/// </summary>
internal static class NativeWindow
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>
    /// Excludes a window from the Alt-Tab switcher by adding the tool-window extended
    /// style. <see cref="Window.ShowInTaskbar"/> alone does not do this. Safe to call once
    /// the window has an HWND (e.g. from <c>OnSourceInitialized</c> or after <c>Show()</c>).
    /// </summary>
    public static void HideFromAltTab(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        _ = SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
