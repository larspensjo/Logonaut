using Logonaut.Theming;
using System;
using System.Windows;

namespace Logonaut.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnSwitchToDarkTheme(object sender, RoutedEventArgs e)
        {
            var themeManager = new ThemeManager();
            themeManager.ApplyTheme(ThemeType.Dark);
        }

        private void OnSwitchToLightTheme(object sender, RoutedEventArgs e)
        {
            var themeManager = new ThemeManager();
            themeManager.ApplyTheme(ThemeType.Light);
        }
    }
}
