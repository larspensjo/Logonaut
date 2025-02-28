using System;
using System.Windows;

namespace Logonaut.Theming
{
    public class ThemeManager
    {
        private const string DarkThemeDictionary = "DarkTheme.xaml";
        private const string LightThemeDictionary = "LightTheme.xaml";

        public ThemeType CurrentTheme { get; private set; } = ThemeType.Light;

        /// <summary>
        /// Applies the specified theme by loading the corresponding ResourceDictionary.
        /// </summary>
        public void ApplyTheme(ThemeType theme)
        {
            try
            {
                var path = GetDictionaryPath(theme);
                Uri themeDictUri = new Uri($"/Logonaut.Theming;component/Themes/{path}", UriKind.Relative);
                ResourceDictionary newTheme = (ResourceDictionary)Application.LoadComponent(themeDictUri);

                // Application.Current.Resources.MergedDictionaries.Clear();
                var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
                mergedDictionaries.Add(newTheme);

                // Remove the old theme AFTER the new one is added
                if (mergedDictionaries.Count > 1)
                {
                    mergedDictionaries.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load theme: {ex.Message}");
            }
            CurrentTheme = theme;
        }

        private string GetDictionaryPath(ThemeType theme)
        {
            return theme == ThemeType.Dark ? DarkThemeDictionary : LightThemeDictionary;
        }
    }
}
