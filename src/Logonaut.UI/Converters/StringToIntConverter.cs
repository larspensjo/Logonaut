using System;
using System.Globalization;
using System.Windows.Data;

namespace Logonaut.UI.Converters // Adjust namespace if needed
{
    public class StringToIntConverter : IValueConverter
    {
        /// <summary>
        /// Converts integer (ViewModel) to string (TextBox).
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Display the integer value as a string in the TextBox
            return value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Converts string (TextBox) back to integer (ViewModel).
        /// Handles empty or invalid input, returning 0.
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string strValue)
            {
                // If the string is empty or just whitespace, treat it as 0
                if (string.IsNullOrWhiteSpace(strValue))
                {
                    return 0;
                }

                // Try to parse the string as an integer
                if (int.TryParse(strValue, NumberStyles.Integer, culture, out int result))
                {
                    // Ensure context lines are not negative
                    return Math.Max(0, result);
                }
            }

            // If conversion fails for any reason (invalid format, wrong type), default to 0
            return 0;
        }
    }
}