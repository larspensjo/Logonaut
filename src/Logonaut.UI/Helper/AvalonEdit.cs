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
    // The binding in MainWindow.xaml connects the view model to the AvalonEditHelper
    
    public static class AvalonEditHelper
    {
        public static readonly DependencyProperty BindableTextProperty =
            DependencyProperty.RegisterAttached(
                "BindableText", // Connected through XAML helpers:AvalonEditHelper.BindableText
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
                "EnableTextBinding", // Connected through XAML helpers:AvalonEditHelper.EnableTextBinding
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

        // Support filter strings for log highlighting
        // ===========================================
        public static readonly DependencyProperty FilterSubstringsProperty =
            DependencyProperty.RegisterAttached(
                "FilterSubstrings", // Connected through XAML helpers:AvalonEditHelper.FilterSubstrings
                typeof(IEnumerable<string>),
                typeof(AvalonEditHelper),
                new PropertyMetadata(null, OnFilterSubstringsChanged));

        public static IEnumerable<string> GetFilterSubstrings(DependencyObject obj)
        {
            return (IEnumerable<string>)obj.GetValue(FilterSubstringsProperty);
        }

        public static void SetFilterSubstrings(DependencyObject obj, IEnumerable<string> value)
        {
            obj.SetValue(FilterSubstringsProperty, value);
        }

        private static void OnFilterSubstringsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextEditor editor && _timestampHighlightingDefinition is CustomHighlightingDefinition customDef)
            {
                var substrings = e.NewValue as IEnumerable<string> ?? Array.Empty<string>();
                customDef.UpdateFilterHighlighting(substrings);
                editor.TextArea.TextView.Redraw();
            }
        }
        
        // Support Timestamp Highlighting
        // ==============================
        public static readonly DependencyProperty HighlightTimestampsProperty =
            DependencyProperty.RegisterAttached(
                "HighlightTimestamps", // Connected through XAML helpers:AvalonEditHelper.HighlightTimestamps
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
                    ApplyAllHighlighting(editor);
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

        private static void ApplyAllHighlighting(TextEditor editor)
        {
            // Create a simple highlighting definition programmatically
            CustomHighlightingDefinition definition = new();
            definition.AddCommonTimestampPatterns();

            // Add custom patterns for log levels
            definition.AddRule(@"\bERROR\b|\bFAILED\b|\bEXCEPTION\b", "error", true);
            definition.AddRule(@"\bWARN\b|\bWARNING\b", "warning", true);
            definition.AddRule(@"\bINFO\b|\bINFORMATION\b", "info", true);

            // Apply any existing filter substrings
            var substrings = GetFilterSubstrings(editor);
            if (substrings != null)
            {
                definition.UpdateFilterHighlighting(substrings);
            }

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
