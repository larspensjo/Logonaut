using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Theming;

namespace Logonaut.UI.ViewModels
{
    public partial class ThemeViewModel : ObservableObject
    {
        private readonly ThemeManager _themeManager;

        public ThemeViewModel()
        {
            _themeManager = new ThemeManager();
            // Assume default theme is Light
            _themeManager.ApplyTheme(ThemeType.Light);
        }

        [RelayCommand]
        private void SwitchToDark()
        {
            _themeManager.ApplyTheme(ThemeType.Dark);
            // Optionally notify other parts of the app that the theme has changed.
        }

        [RelayCommand]
        private void SwitchToLight()
        {
            _themeManager.ApplyTheme(ThemeType.Light);
        }
    }
}
