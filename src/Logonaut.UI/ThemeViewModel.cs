using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Logonaut.Theming;

namespace Logonaut.UI.ViewModels
{
    public class ThemeViewModel : ViewModelBase
    {
        private readonly ThemeManager _themeManager;

        public RelayCommand SwitchToDarkCommand { get; }
        public RelayCommand SwitchToLightCommand { get; }

        public ThemeViewModel()
        {
            // var app = System.Windows.Application.LoadComponent();
            _themeManager = new ThemeManager();
            // Assume default theme is Light
            _themeManager.ApplyTheme(ThemeType.Light);
            SwitchToDarkCommand = new RelayCommand(SwitchToDark);
            SwitchToLightCommand = new RelayCommand(SwitchToLight);
        }

        private void SwitchToDark()
        {
            _themeManager.ApplyTheme(ThemeType.Dark);
            // Optionally notify other parts of the app that the theme has changed.
        }

        private void SwitchToLight()
        {
            _themeManager.ApplyTheme(ThemeType.Light);
        }
    }
}
