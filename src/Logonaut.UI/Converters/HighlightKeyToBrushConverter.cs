using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Logonaut.UI.Converters
{
    public class HighlightKeyToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorKey)
            {
                // Determine if we should fetch background or foreground
                // Default to Background if parameter is not "Foreground"
                string brushSuffix = ".Background";
                if (parameter is string paramStr && paramStr.Equals("Foreground", StringComparison.OrdinalIgnoreCase))
                {
                    brushSuffix = ".Foreground";
                }

                string resourceKey = colorKey + brushSuffix;
                Brush? themeBrush = Application.Current?.TryFindResource(resourceKey) as Brush;

                return themeBrush ?? Brushes.Transparent; // Fallback to transparent if not found
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
