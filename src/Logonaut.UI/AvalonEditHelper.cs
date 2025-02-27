using System;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using System.Windows.Controls;

namespace Logonaut.UI.Helpers
{
    public static class AvalonEditHelper
    {
        public static readonly DependencyProperty BindableTextProperty =
            DependencyProperty.RegisterAttached(
                "BindableText",
                typeof(string),
                typeof(AvalonEditHelper),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindableTextChanged));

        public static string GetBindableText(DependencyObject obj)
        {
            return (string)obj.GetValue(BindableTextProperty);
        }

        public static void SetBindableText(DependencyObject obj, string value)
        {
            obj.SetValue(BindableTextProperty, value);
        }

        private static void OnBindableTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextEditor editor)
            {
                string newText = e.NewValue as string ?? string.Empty;
                // Avoid recursive updates
                if (editor.Text != newText)
                {
                    editor.Text = newText;
                }
            }
        }

        // Optional: If you want two-way binding, subscribe to the TextChanged event.
        public static readonly DependencyProperty EnableTextBindingProperty =
            DependencyProperty.RegisterAttached(
                "EnableTextBinding",
                typeof(bool),
                typeof(AvalonEditHelper),
                new PropertyMetadata(false, OnEnableTextBindingChanged));

        public static bool GetEnableTextBinding(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableTextBindingProperty);
        }

        public static void SetEnableTextBinding(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableTextBindingProperty, value);
        }

        private static void OnEnableTextBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextEditor editor)
            {
                bool enable = (bool)e.NewValue;
                if (enable)
                {
                    editor.TextChanged += Editor_TextChanged;
                }
                else
                {
                    editor.TextChanged -= Editor_TextChanged;
                }
            }
        }

        private static void Editor_TextChanged(object sender, EventArgs e)
        {
            if (sender is TextEditor editor)
            {
                SetBindableText(editor, editor.Text);
            }
        }
    }
}
