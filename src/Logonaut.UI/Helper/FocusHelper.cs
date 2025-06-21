using System.Windows;
using System.Windows.Controls;

namespace Logonaut.UI.Helpers;

/*
 * Provides an attached property to reliably set focus on a UI element from a ViewModel.
 * This helper is used to solve a common WPF issue where programmatically setting focus on an
 * element inside a DataTemplate (especially one whose visibility is controlled by a binding)
 * can be unreliable due to timing and lifecycle issues.
 *
 * The `FocusHelper.IsFocused` attached property can be bound to a boolean property in a ViewModel.
 * When the ViewModel property becomes true, this helper ensures the associated UI element
 * receives focus and, in the case of a TextBox, selects all its content.
 */
public static class FocusHelper
{
    public static readonly DependencyProperty IsFocusedProperty =
        DependencyProperty.RegisterAttached(
            "IsFocused",
            typeof(bool),
            typeof(FocusHelper),
            new PropertyMetadata(false, OnIsFocusedPropertyChanged));

    public static bool GetIsFocused(DependencyObject obj)
    {
        return (bool)obj.GetValue(IsFocusedProperty);
    }

    public static void SetIsFocused(DependencyObject obj, bool value)
    {
        obj.SetValue(IsFocusedProperty, value);
    }

    private static void OnIsFocusedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Early-out if the object is not a TextBox or the new value is not true.
        if (d is not TextBox textBox || (bool)e.NewValue == false)
        {
            return;
        }

        // Use the dispatcher to queue the focus-setting logic.
        // This ensures the action occurs after the current layout pass is complete,
        // which is crucial when the TextBox has just become visible.
        textBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }));
    }
}