using System;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using System.Windows.Controls;
using System.IO;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;

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

        private static void Editor_TextChanged(object? sender, EventArgs e)
        {
            if (sender is TextEditor editor)
            {
                SetBindableText(editor, editor.Text);
            }
        }
        
        // Support Timestamp Highlighting
        // ==============================
        public static readonly DependencyProperty HighlightTimestampsProperty =
            DependencyProperty.RegisterAttached(
                "HighlightTimestamps",
                typeof(bool),
                typeof(AvalonEditHelper),
                new PropertyMetadata(false, OnHighlightTimestampsChanged));

        public static bool GetHighlightTimestamps(DependencyObject obj)
        {
            return (bool)obj.GetValue(HighlightTimestampsProperty);
        }

        public static void SetHighlightTimestamps(DependencyObject obj, bool value)
        {
            obj.SetValue(HighlightTimestampsProperty, value);
        }

        private static void OnHighlightTimestampsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextEditor editor)
            {
                bool highlight = (bool)e.NewValue;
                
                // Apply or remove timestamp highlighting
                if (highlight)
                {
                    // Apply custom highlighting for timestamps
                    editor.IsReadOnly = false;
                    ApplyTimestampHighlighting(editor);
                    editor.TextArea.TextView.Redraw(); // Force redraw to apply highlighting
                }
                else
                {
                    // Restore default highlighting
                    RestoreDefaultHighlighting(editor);
                    editor.TextArea.TextView.Redraw(); // Force redraw to remove highlighting
                }
            }
        }
        private static IHighlightingDefinition? _timestampHighlightingDefinition;

        private static void ApplyTimestampHighlighting(TextEditor editor)
        {
            // Create a simple highlighting definition programmatically
            CustomHighlightingDefinition definition = new();
            definition.AddCommonTimestampPatterns();

            // Add custom patterns for log levels
            definition.AddRule(@"\bERROR\b|\bFAILED\b|\bEXCEPTION\b", "error", true);
            definition.AddRule(@"\bWARN\b|\bWARNING\b", "warning", true);
            definition.AddRule(@"\bINFO\b|\bINFORMATION\b", "info", true);

            _timestampHighlightingDefinition = definition;
            
            // Apply the highlighting definition to the editor
            editor.SyntaxHighlighting = _timestampHighlightingDefinition;
            editor.TextArea.TextView.Redraw();
        }

        private static void RestoreDefaultHighlighting(TextEditor editor)
        {
            // Restore the default "Log" highlighting
            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Log");
        }
    }
}
