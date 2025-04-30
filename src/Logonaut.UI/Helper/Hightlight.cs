using System.Windows;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media;

namespace Logonaut.UI.Helpers;

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

    // Track the search highlighting rule separately
    private HighlightingRule? _searchHighlightingRule = null;

    public IEnumerable<HighlightingColor> NamedHighlightingColors => _namedColors.Values;

    public IDictionary<string, string> Properties => _properties;

    public CustomHighlightingDefinition()
    {
        // TODO: Clean-up here.
        // TODO: Should defaultl colors be set in the constructor or in a separate method? Maybe in the themes?
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
        
        // Ensure the 'filter' and 'searchMatch' colors use theme resources
        _namedColors["filter"] = new HighlightingColor
        {
            Background = new SimpleHighlightingBrush(((Application.Current?.TryFindResource("Highlighting.FilterMatch.Background") as SolidColorBrush)?.Color ?? Colors.Yellow)), // Fallback
            Foreground = new SimpleHighlightingBrush(((Application.Current?.TryFindResource("Highlighting.FilterMatch.Foreground") as SolidColorBrush)?.Color ?? Colors.Black)) // Fallback
        };

        _namedColors["searchMatch"] = new HighlightingColor
        {
            Background = new SimpleHighlightingBrush(((Application.Current?.TryFindResource("Highlighting.SearchMatch.Background") as SolidColorBrush)?.Color ?? Colors.LightCyan)), // Fallback
            Foreground = new SimpleHighlightingBrush(((Application.Current?.TryFindResource("Highlighting.SearchMatch.Foreground") as SolidColorBrush)?.Color ?? Colors.Black)) // Fallback
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

    // Method to update/create/remove the search term highlighting rule
    public void UpdateSearchHighlighting(string? searchTerm, bool matchCase = false)
    {
        // Remove existing search rule if present
        if (_searchHighlightingRule != null)
        {
            MainRuleSet.Rules.Remove(_searchHighlightingRule);
            _searchHighlightingRule = null;
        }

        // Add new rule if search term is valid
        if (!string.IsNullOrEmpty(searchTerm))
        {
            try
            {
                // Escape the search term to treat it literally unless it's intended as regex
                // For simple search, escaping is safer.
                string escapedSearchTerm = Regex.Escape(searchTerm);

                _searchHighlightingRule = new HighlightingRule
                {
                    Color = _namedColors["searchMatch"],
                    Regex = new Regex(escapedSearchTerm, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase)
                };
                MainRuleSet.Rules.Add(_searchHighlightingRule);
            }
            catch (ArgumentException ex)
            {
                // Handle invalid regex resulting from escaping (highly unlikely but possible)
                _searchHighlightingRule = null; // Ensure it's null
                // TODO: Better error handling
                System.Diagnostics.Debug.WriteLine($"Error creating search regex for '{searchTerm}': {ex.Message}");
            }
        }
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
            // TODO: There is a bug that makes this happen sometimes.
            if (substring == "")
                throw new ArgumentException("Filter substring cannot be empty");
            try
            {
                // Create a regex from the substring (which might already be a regex pattern)
                var rule = new HighlightingRule
                {
                    Color = _namedColors["filter"],
                    Regex = new Regex(substring, RegexOptions.IgnoreCase)
                };
                if (rule.Regex.IsMatch(string.Empty))
                    throw new ArgumentException("Filter substring cannot be empty");
                _filterHighlightingRules.Add(rule);
                MainRuleSet.Rules.Add(rule);
            }
            catch (ArgumentException)
            {
                // Skip invalid regex patterns
                // TODO: This should be an exception in the future
                System.Diagnostics.Debug.WriteLine($"Invalid regex pattern: {substring}");
            }
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
