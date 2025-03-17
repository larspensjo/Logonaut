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
        
        // Track filter-based highlighting rules separately
        private List<HighlightingRule> _filterHighlightingRules = new();

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
            
            // Add a special color for filter matches
            _namedColors["filter"] = new HighlightingColor 
            { 
                Background = new SimpleHighlightingBrush(Colors.Yellow),
                Foreground = new SimpleHighlightingBrush(Colors.Black)
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
            
            // Date with time: 2025-02-25 06:15:23
            AddTimestampPattern(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
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
        
        // Update filter-based highlighting with a new set of substrings
        // TODO: Instead of clearing old rules, make new ones. This function isn't needed.
        public void UpdateFilterHighlighting(IEnumerable<string> filterSubstrings)
        {
            // Remove all existing filter highlighting rules
            foreach (var rule in _filterHighlightingRules)
            {
                MainRuleSet.Rules.Remove(rule);
            }
            _filterHighlightingRules.Clear();
            
            // Add new rules for each filter substring
            foreach (var substring in filterSubstrings)
            {
                AddFilterSubstringHighlighting(substring);
            }
        
            // Add highlighting for a filter substring
            void AddFilterSubstringHighlighting(string substring)
            {
                // Escape special regex characters in the substring
                string escapedSubstring = Regex.Escape(substring);
                
                var rule = new HighlightingRule
                {
                    Color = _namedColors["filter"],
                    Regex = new Regex(escapedSubstring, RegexOptions.IgnoreCase)
                };
                
                _filterHighlightingRules.Add(rule);
                MainRuleSet.Rules.Add(rule);
            }
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
