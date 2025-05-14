using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Logonaut.UI.Converters
{
    public class IsFilterTypeConverter : IMultiValueConverter
    {
        public static IsFilterTypeConverter Instance { get; } = new IsFilterTypeConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 0 && values[0] is string currentFilterType && parameter is string allowedTypesParam)
            {
                var allowedTypes = allowedTypesParam.Split('|');
                return allowedTypes.Contains(currentFilterType);
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
