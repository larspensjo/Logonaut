using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls;

namespace Logonaut.Theming.Converters
{
    /// <summary>
    /// Converts TreeViewItem depth to left margin.
    /// Needed for TreeViewItem Style.
    /// </summary>
    public class TreeViewIndentConverter : IValueConverter
    {
        public double Indent { get; set; } = 19.0; // Default indent per level

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // The 'value' is now the TreeViewItem itself due to the changed binding
            if (value is TreeViewItem item)
            {
                int depth = GetDepth(item);
                return new Thickness(depth * Indent, 0, 0, 0);
            }
            return new Thickness(0); // Default if not a TreeViewItem or error
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        // Helper method to calculate the depth of a TreeViewItem
        private int GetDepth(DependencyObject item)
        {
            int depth = 0;
            DependencyObject? parent = VisualTreeHelper.GetParent(item);

            while (parent != null)
            {
                // Only count TreeViewItem ancestors
                if (parent is TreeViewItem)
                {
                    depth++;
                }
                // Stop if we reach the TreeView itself or run out of parents
                else if (parent is TreeView)
                {
                    break;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return depth;
        }
    }

    // TODO: Need ExpandCollapseToggleStyle definition as well for TreeViewItem
    // Add this style to both DarkTheme.xaml and LightTheme.xaml

} // End of namespace