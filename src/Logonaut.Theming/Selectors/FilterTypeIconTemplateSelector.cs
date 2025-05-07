using System.Windows;
using System.Windows.Controls;

namespace Logonaut.Theming.Selectors;

public class FilterTypeIconTemplateSelector : DataTemplateSelector
{
    // Define properties for each template if you want to set them from XAML,
    // or use FindResource as shown below. For simplicity, FindResource is often easier.

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        FrameworkElement? element = container as FrameworkElement;
        if (element != null && item is string typeIdentifier)
        {
            // Attempt to find the DataTemplate resource by key
            // The key will be like "SubstringIconTemplate", "RegexIconTemplate", etc.
            object? resource = element.TryFindResource(typeIdentifier + "IconTemplate");
            if (resource is DataTemplate dataTemplate)
            {
                return dataTemplate;
            }
        }
        // Fallback or default template if needed
        return base.SelectTemplate(item, container); 
    }
}
