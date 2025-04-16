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
            var path = GetDictionaryPath(theme);
            Uri themeDictUri = new Uri($"/Logonaut.Theming;component/Themes/{path}", UriKind.Relative);
            ResourceDictionary newTheme = new ResourceDictionary { Source = themeDictUri };

            // Add the new theme dictionary.
            var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
            mergedDictionaries.Add(newTheme);

            // Remove any old theme dictionaries (those with a Source that contains "/Themes/")
            // Exclude the new one we just added.
            for (int i = mergedDictionaries.Count - 2; i >= 0; i--)
            {
                var dict = mergedDictionaries[i];
                if (dict.Source != null && dict.Source.OriginalString.Contains("/Themes/"))
                {
                    mergedDictionaries.RemoveAt(i);
                }
            }
            CurrentTheme = theme;
        }

        private string GetDictionaryPath(ThemeType theme)
        {
            return theme == ThemeType.Dark ? DarkThemeDictionary : LightThemeDictionary;
        }
    }
}
