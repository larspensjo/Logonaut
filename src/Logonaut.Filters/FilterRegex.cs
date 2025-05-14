using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Logonaut.Filters;

/// <summary>
/// A filter that checks whether a log line matches a specified regular expression.
/// </summary>
public class RegexFilter : IFilter
{
    public bool Enabled { get; set; } = true;
    private string _pattern = string.Empty;
    private Regex? _regex;
    private bool _isCaseSensitive;

    public bool IsEditable => true;

    // This property name "Value" is what gets serialized
    [JsonProperty] public string Value
    {
        get => _pattern;
        set
        {
            _pattern = value ?? string.Empty;
            UpdateRegex();
        }
    }

    [JsonProperty] public bool IsCaseSensitive
    {
        get => _isCaseSensitive;
        set
        {
            if (_isCaseSensitive != value)
            {
                _isCaseSensitive = value;
                UpdateRegex();
            }
        }
    }

    [JsonProperty] public string HighlightColorKey { get; set; } = "FilterHighlight.Default";

    [JsonConstructor] public RegexFilter(string value, bool isCaseSensitive = false, string? highlightColorKey = null)
    {
        _pattern = value ?? string.Empty; 
        _isCaseSensitive = isCaseSensitive;
        HighlightColorKey = string.IsNullOrEmpty(highlightColorKey) ? "FilterHighlight.Default" : highlightColorKey;
        UpdateRegex();
    }
    
    // No parameterless constructor needed if JsonConstructor is used effectively

    private void UpdateRegex()
    {
        try
        {
            // Handle potentially empty pattern gracefully
            if (string.IsNullOrEmpty(_pattern)) {
                    _regex = null; // Or a regex that matches nothing: new Regex("(?!)");
                    return;
            }
            RegexOptions options = IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            _regex = new Regex(_pattern, options);
        }
        catch (ArgumentException ex)
        {
            // TODO: We need to show this to the user in a better way.
            _regex = null; // Invalid regex pattern, set to null
            Debug.WriteLine($"Invalid regex pattern: {_pattern}. Exception: {ex.Message}");
        }
    }

    public bool IsMatch(string line)
    {
        // If Enabled is false, we treat it as a match (neutral filter).
        if (!Enabled) return true;

        // If regex is null (due to invalid/empty pattern), it cannot match anything.
        if (_regex == null) return false;

        // If line is null, it cannot match.
        if (line == null) return false;

        try
        {
            return _regex.IsMatch(line);
        }
        catch (Exception ex) // Catch potential runtime regex errors
        {
            // TODO: Some imporved user interaction is needed here.
            System.Diagnostics.Debug.WriteLine($"Regex runtime error matching '{_pattern}' against line: {ex.Message}");
            return false; // Treat runtime errors as non-matches
        }
    }

    // DisplayText uses the _pattern field, which is fine.
    public string DisplayText => _regex == null ? "/(Invalid)/" : $"/{_pattern}/";

    public string TypeText => "RegexType";
}
