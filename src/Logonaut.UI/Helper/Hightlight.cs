using System.Windows;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media;
using Logonaut.Filters;

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
    private List<KeyValuePair<IFilter, HighlightingRule>> _filterHighlightingRules = new();

    // Track the search highlighting rule separately
    private HighlightingRule? _searchHighlightingRule = null;

    public IEnumerable<HighlightingColor> NamedHighlightingColors => _namedColors.Values;

    public IDictionary<string, string> Properties => _properties;

    public CustomHighlightingDefinition()
    {
        // Initialize colors from theme resources
        RefreshColorsFromTheme();
    }

    // Method to (re)load colors from theme resources
    public void RefreshColorsFromTheme()
    {
        _namedColors["timestamp"] = new HighlightingColor 
        { 
            Foreground = GetThemeBrush("Highlighting.Timestamp", Colors.DarkBlue), // Fallback color
            FontWeight = FontWeights.Bold // Keep bold for timestamps
        };
        
        _namedColors["error"] = new HighlightingColor 
        { 
            Foreground = GetThemeBrush("Highlighting.Error", Colors.Red), // Fallback color
            FontWeight = FontWeights.Bold // Keep bold for errors
        };
        
        _namedColors["warning"] = new HighlightingColor 
        { 
            Foreground = GetThemeBrush("Highlighting.Warning", Colors.Orange) // Fallback color
        };
        
        _namedColors["info"] = new HighlightingColor 
        { 
            // For 'info', the theme files use "InfoColor" for the brush, which is fine.
            // But AvalonEdit expects "Highlighting.Info". We can either align names
            // or use the existing "InfoColor" key if it's distinct enough.
            // Let's assume the theme file has a "Highlighting.Info" brush defined for consistency.
            Foreground = GetThemeBrush("Highlighting.Info", Colors.Green) // Fallback color
        };

        _namedColors["searchMatch"] = new HighlightingColor
        {
            Background = GetThemeBrush("Highlighting.SearchMatch.Background", Colors.LightCyan),
            Foreground = GetThemeBrush("Highlighting.SearchMatch.Foreground", Colors.Black)
        };
    }

    // Helper to get brush from theme resources with a fallback
    private SimpleHighlightingBrush GetThemeBrush(string resourceKey, Color fallbackColor)
    {
        Brush? themeBrush = Application.Current?.TryFindResource(resourceKey) as Brush;
        if (themeBrush is SolidColorBrush scb)
        {
            return new SimpleHighlightingBrush(scb.Color);
        }
        // Add handling for other brush types if necessary, or default to SolidColorBrush
        return new SimpleHighlightingBrush(fallbackColor);
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
    public void UpdateFilterHighlighting(IEnumerable<IFilter> filterModels)
    {
        // Remove all existing filter highlighting rules
        foreach (var kvp in _filterHighlightingRules)
        {
            MainRuleSet.Rules.Remove(kvp.Value);
        }
        _filterHighlightingRules.Clear();
        
        // Add new rules for each enabled, non-empty filter model
        foreach (var filterModel in filterModels)
        {
            if (filterModel.Enabled && !string.IsNullOrEmpty(filterModel.Value))
            {
                AddSingleFilterHighlightRule(filterModel);
            }
        }

        // Adds a highlighting rule for a single IFilter model
        void AddSingleFilterHighlightRule(IFilter filterModel)
        {
            if (string.IsNullOrEmpty(filterModel.Value)) // Should be caught by caller, but double check
                return;

            try
            {
                string pattern = filterModel.Value;
                bool isRegexFilter = filterModel is RegexFilter; // Check if it's a RegexFilter

                // For SubstringFilter, escape the pattern to treat it literally
                if (!isRegexFilter)
                {
                    pattern = Regex.Escape(pattern);
                }

                // Resolve the HighlightColor for this specific filter
                HighlightingColor? colorForThisFilter = ResolveHighlightColor(filterModel.HighlightColorKey);

                if (colorForThisFilter == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Could not resolve highlight color for key '{filterModel.HighlightColorKey}'. Using default.");
                    // Fallback to a default if the key isn't found (e.g., if "filter_default_background" is defined)
                    colorForThisFilter = _namedColors["filter_default_background"]; 
                }
                
                var rule = new HighlightingRule
                {
                    Color = colorForThisFilter,
                    Regex = new Regex(pattern, RegexOptions.IgnoreCase) // Assuming IgnoreCase for filters for now
                };

                // This check was for the old UpdateFilterHighlighting that took string patterns.
                // Now, filterModel.Value for RegexFilter is the pattern itself.
                // For SubstringFilter, it's escaped.
                // An empty Regex pattern (e.g. from an empty SubstringFilter.Value)
                // would have Regex.Escape("") -> "", which is a valid Regex that matches empty strings.
                // It's better to filter out empty filterModel.Value before calling this.
                // if (rule.Regex.IsMatch(string.Empty) && isRegexFilter) // More specific check for regex
                // {
                //     System.Diagnostics.Debug.WriteLine($"Warning: Regex pattern '{filterModel.Value}' for filter matches empty string. Skipping highlight rule.");
                //     return;
                // }
                    
                _filterHighlightingRules.Add(new KeyValuePair<IFilter, HighlightingRule>(filterModel, rule));
                MainRuleSet.Rules.Add(rule);
            }
            catch (ArgumentException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid regex pattern for filter '{filterModel.Value}': {ex.Message}. Skipping highlight rule.");
            }
        }

        // Dynamically resolves HighlightingColor based on resource keys
        HighlightingColor? ResolveHighlightColor(string colorKey)
        {
            Brush? backgroundBrush = Application.Current?.TryFindResource(colorKey + ".Background") as Brush;
            Brush? foregroundBrush = Application.Current?.TryFindResource(colorKey + ".Foreground") as Brush;

            // If brushes are not found, this filter might not get custom coloring or fallback.
            if (backgroundBrush == null && foregroundBrush == null) return null;

            var highlightingColor = new HighlightingColor();
            if (backgroundBrush is SolidColorBrush bgScb)
                highlightingColor.Background = new SimpleHighlightingBrush(bgScb.Color);
            if (foregroundBrush is SolidColorBrush fgScb)
                highlightingColor.Foreground = new SimpleHighlightingBrush(fgScb.Color);
            
            // If only one is found, the other remains null (default behavior by AvalonEdit)
            return highlightingColor;
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
