using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Logonaut.UI
{
    public class BooleanToHighlightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return new SolidColorBrush(Colors.LightGray); // Highlight color
            }
            return new SolidColorBrush(Colors.Transparent); // Default color
        }

        // TODO: Is this needed?
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
