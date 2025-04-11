using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media; // Required for Geometry
using System.Windows.Shapes; // Required for Path

namespace Logonaut.UI.Converters
{
    public class FilterTypeToIconConverter : IValueConverter
    {
        // Define Geometry resources (or load from external source/resource dictionary)
        // Example geometries (replace with actual SVG paths or better representations)
        private static readonly Geometry SubstringIcon = Geometry.Parse("M 5,5 L 15,5 M 5,10 L 15,10 M 5,15 L 15,15"); // Resembles text lines
        private static readonly Geometry RegexIcon = Geometry.Parse("M 3,3 C 8,15 12,5 17,17"); // Abstract curvy line
        private static readonly Geometry AndIcon = Geometry.Parse("M 5,15 L 10,5 L 15,15 M 7.5,11 H 12.5"); // 'A' like shape for AND
        private static readonly Geometry OrIcon = Geometry.Parse("M 5,5 L 10,15 L 15,5"); // 'V' like shape for OR
        private static readonly Geometry NorIcon = Geometry.Parse("M 5,5 L 10,15 L 15,5 M 3,2 H 17"); // 'V' with a bar over it
        private static readonly Geometry TrueIcon = Geometry.Parse("M 5,10 L 9,15 L 15,5"); // Checkmark

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string typeText)
            {
                Geometry? iconGeometry = typeText switch
                {
                    "SubstringType" => SubstringIcon,
                    "RegexType" => RegexIcon,
                    "AndType" => AndIcon,
                    "OrType" => OrIcon,
                    "NorType" => NorIcon,
                    "TRUE" => TrueIcon, // Assuming FilterViewModel returns "TRUE" for TrueFilter
                    _ => null
                };

                if (iconGeometry != null)
                {
                    // Return a Path element using the geometry
                    // Use DynamicResource for Fill color for theming
                    var path = new Path { Data = iconGeometry, Stretch = Stretch.Uniform, Width = 12, Height = 12 };
                    path.SetResourceReference(Shape.FillProperty, "TextForegroundBrush"); // Bind fill to theme text color
                    return path;
                }
            }
            return null; // Return null if no icon is defined or value is wrong type
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}