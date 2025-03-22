using System;
using System.Text.RegularExpressions;

namespace Logonaut.Filters
{
    /// <summary>
    /// A filter that checks whether a log line matches a specified regular expression.
    /// </summary>
    public class RegexFilter : IFilter
    {
        public bool Enabled { get; set; } = true;
        private string _pattern;
        private Regex? _regex;
        private bool _isCaseSensitive;

        public bool IsEditable => true;

        // The value is the search pattern.
        public string Value
        {
            get => _pattern;
            set
            {
                _pattern = value;
                UpdateRegex();
            }
        }

        public bool IsCaseSensitive
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

        public RegexFilter(string pattern, bool isCaseSensitive = false)
        {
            _pattern = pattern;
            _isCaseSensitive = isCaseSensitive;
            UpdateRegex();
        }

        private void UpdateRegex()
        {
            try
            {
                RegexOptions options = _isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                _regex = new Regex(_pattern, options);
            }
            catch (ArgumentException)
            {
                // Handle invalid regex pattern
                _regex = null;
            }
        }

        public bool IsMatch(string line)
        {
            if (!Enabled)
                return true; // Disabled filters are treated as neutral.
            
            if (line == null || _regex == null)
                return false;

            return _regex.IsMatch(line);
        }

        public string DisplayText => $"/{_pattern}/";

        public string TypeText => "RegexType";
    }
}
