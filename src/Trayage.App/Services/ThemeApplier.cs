using Trayage.Core.Configuration;
using Wpf.Ui.Appearance;

namespace Trayage.App.Services;

/// <summary>Applies the user's chosen <see cref="AppTheme"/> to the WPF-UI theme manager.</summary>
public static class ThemeApplier
{
    public static void Apply(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}
