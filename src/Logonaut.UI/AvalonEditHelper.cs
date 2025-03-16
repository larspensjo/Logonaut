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
        private static IHighlightingDefinition LoadHighlightingDefinitionFromResource(string resourcePath)
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "TimestampHighlighting.xshd");
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Highlighting file not found: {filePath}");

            using (Stream s = File.OpenRead(filePath))
            {
                using (XmlReader reader = new XmlTextReader(s))
                {
                    // First load the XSHD document
                    var xshd = HighlightingLoader.LoadXshd(reader);
                    
                    // Then convert it to a highlighting definition
                    var highlighting = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                    return highlighting;
                }
            }
        }

        private static void ApplyTimestampHighlighting(TextEditor editor)
        {
            // Load the highlighting definition from resource if not already loaded
            if (_timestampHighlightingDefinition == null)
            {
                try
                {
                    _timestampHighlightingDefinition = LoadHighlightingDefinitionFromResource("Resources/TimestampHighlighting.xshd");
                }
                catch (Exception ex)
                {
                    // TODO: Log or handle the exception as appropriate for your application
                    Console.WriteLine($"Failed to load timestamp highlighting: {ex.Message}");
                    return;
                }
            }
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
        public string Name => "TimestampHighlighting";
        public HighlightingRuleSet MainRuleSet { get; } = new HighlightingRuleSet();
        
        public HighlightingColor DefaultTextColor { get; set; } = new HighlightingColor 
        { 
            Foreground = new SimpleHighlightingBrush(Colors.Red) 
        };

        public IEnumerable<HighlightingColor> NamedHighlightingColors => 
            new[] { DefaultTextColor };

        public IDictionary<string, string> Properties => 
            new Dictionary<string, string>();

        public HighlightingRuleSet? GetNamedRuleSet(string name) => null;
        public HighlightingColor? GetNamedColor(string name) => null;
    }

}
