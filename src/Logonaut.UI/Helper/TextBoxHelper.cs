using System.Windows;
using System.Windows.Controls;

namespace Logonaut.UI.Helpers;

// The TextBoxHelper class is a static helper class that provides an attached property FocusOnVisible for TextBox controls in WPF applications.
// The purpose of this class is to automatically set focus and select all text in a TextBox when it becomes visible.
public static class TextBoxHelper
{
    // Define the FocusOnVisible attached property
    public static readonly DependencyProperty FocusOnVisibleProperty =
        DependencyProperty.RegisterAttached(
            "FocusOnVisible",
            typeof(bool),
            typeof(TextBoxHelper),
            new PropertyMetadata(false, OnFocusOnVisibleChanged));

    // Getter for FocusOnVisible property
    public static bool GetFocusOnVisible(DependencyObject obj)
    {
        return (bool)obj.GetValue(FocusOnVisibleProperty);
    }

    // Setter for FocusOnVisible property
    public static void SetFocusOnVisible(DependencyObject obj, bool value)
    {
        obj.SetValue(FocusOnVisibleProperty, value);
    }

    // Callback when FocusOnVisible property changes
    private static void OnFocusOnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox && (bool)e.NewValue)
        {
            textBox.IsVisibleChanged += TextBox_IsVisibleChanged;
        }
    }

    // Event handler for IsVisibleChanged event
    private static void TextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && (bool)e.NewValue)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }
}
