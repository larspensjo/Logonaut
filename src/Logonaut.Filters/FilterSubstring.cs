using Newtonsoft.Json;

namespace Logonaut.Filters
{
    /// <summary>
    /// A filter that checks whether a log line contains a specified substring.
    /// </summary>
     public class SubstringFilter : IFilter
    {
        public bool Enabled { get; set; } = true;
        // The value is the substring.
        public string Value { get; set; }
        
        public bool IsEditable => true;

        [JsonProperty] public string HighlightColorKey { get; set; } = "FilterHighlight.Default";

        public SubstringFilter(string substring)
        {
            Value = substring;
        }

        // Add JsonConstructor for deserialization if needed, or ensure properties are settable
        [JsonConstructor] public SubstringFilter(string value, string highlightColorKey)
        {
            Value = value;
            HighlightColorKey = string.IsNullOrEmpty(highlightColorKey) ? "FilterHighlight.Default" : highlightColorKey;
        }


        public bool IsMatch(string line)
        {
            if (!Enabled)
                return true; // Disabled filters are treated as neutral.
            if (line == null || Value == null)
                return false;
                
            return line.Contains(Value);
        }

        public string DisplayText => $"\"{Value}\"";

        public string TypeText => "SubstringType";
    }
}
