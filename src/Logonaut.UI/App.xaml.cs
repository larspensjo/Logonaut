using Logonaut.Theming;
using System.Windows;

namespace Logonaut.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // This is loaded by App.xaml
        // Create the theme manager with the loader.
        //var themeManager = new ThemeManager();
        // Apply the dark theme by default.
        //themeManager.ApplyTheme(ThemeType.Light);
    }
}
