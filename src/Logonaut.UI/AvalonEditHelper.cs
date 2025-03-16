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
            IHighlightingDefinition definition = new CustomHighlightingDefinition();
            
            // Add a simple rule
            HighlightingRule rule = new HighlightingRule();
            rule.Color = new HighlightingColor { Foreground = new SimpleHighlightingBrush(Colors.Red) };
            rule.Regex = new Regex(".+"); // Match everything
            
            definition.MainRuleSet.Rules.Add(rule);
#if true
            // Overwrite with a debug highlighter.
            _timestampHighlightingDefinition = definition;
#endif
            
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

    public class CustomHighlightingDefinition : IHighlightingDefinition
    {
        public string Name => "DynamicHighlighting";
        public HighlightingRuleSet MainRuleSet { get; } = new();
        
        // Default text color if no highlighting is applied
        public HighlightingColor DefaultTextColor { get; set; } = new HighlightingColor 
        { 
            Foreground = new SimpleHighlightingBrush(Colors.Black) 
        };

        // Collection of named colors that can be referenced
        private Dictionary<string, HighlightingColor> _namedColors = new();
        
        // Properties dictionary for additional configuration
        private Dictionary<string, string> _properties = new();

        public IEnumerable<HighlightingColor> NamedHighlightingColors => _namedColors.Values;

        public IDictionary<string, string> Properties => _properties;

        public CustomHighlightingDefinition()
        {
            // Initialize with some default named colors
            _namedColors["timestamp"] = new HighlightingColor 
            { 
                Foreground = new SimpleHighlightingBrush(Colors.DarkBlue),
                FontWeight = FontWeights.Bold
            };
            
            _namedColors["error"] = new HighlightingColor 
            { 
                Foreground = new SimpleHighlightingBrush(Colors.Red),
                FontWeight = FontWeights.Bold
            };
            
            _namedColors["warning"] = new HighlightingColor 
            { 
                Foreground = new SimpleHighlightingBrush(Colors.Orange)
            };
            
            _namedColors["info"] = new HighlightingColor 
            { 
                Foreground = new SimpleHighlightingBrush(Colors.Green)
            };
        }

        // Add a highlighting rule with a specific pattern and color
        public void AddRule(string pattern, string colorName, bool isCaseSensitive = false)
        {
            if (!_namedColors.ContainsKey(colorName))
                throw new ArgumentException($"Color name '{colorName}' is not defined");

            var rule = new HighlightingRule
            {
                Color = _namedColors[colorName],
                Regex = new Regex(pattern, isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
            };
            
            MainRuleSet.Rules.Add(rule);
        }

        // Add a timestamp pattern with the timestamp color
        public void AddTimestampPattern(string pattern)
        {
            AddRule(pattern, "timestamp");
        }

        // Add common timestamp patterns
        public void AddCommonTimestampPatterns()
        {
            // ISO 8601 date/time format: 2023-10-15T14:30:15
            AddTimestampPattern(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}");
            
            // Common log timestamp: [2023-10-15 14:30:15]
            AddTimestampPattern(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]");
            
            // Simple time format: 14:30:15
            AddTimestampPattern(@"^\d{2}:\d{2}:\d{2}");
            
            // Date with time: 2023/10/15 14:30:15
            AddTimestampPattern(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}");
        }

        // Add a custom color definition
        public void AddNamedColor(string name, Color foreground, Color? background = null, 
                                FontWeight? fontWeight = null, FontStyle? fontStyle = null)
        {
            var color = new HighlightingColor
            {
                Foreground = new SimpleHighlightingBrush(foreground)
            };
            
            if (background.HasValue)
                color.Background = new SimpleHighlightingBrush(background.Value);
                
            if (fontWeight.HasValue)
                color.FontWeight = fontWeight.Value;
                
            if (fontStyle.HasValue)
                color.FontStyle = fontStyle.Value;
                
            _namedColors[name] = color;
        }

        // Clear all rules
        public void ClearRules()
        {
            MainRuleSet.Rules.Clear();
        }

        // Implementation of IHighlightingDefinition interface methods
        public HighlightingRuleSet? GetNamedRuleSet(string name) => null;
        
        public HighlightingColor? GetNamedColor(string name)
        {
            if (_namedColors.TryGetValue(name, out var color))
                return color;
            return null;
        }
    }

}
