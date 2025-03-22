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

        public SubstringFilter(string substring)
        {
            Value = substring;
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
