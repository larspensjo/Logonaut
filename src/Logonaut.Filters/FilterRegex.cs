using System;
using System.Text.RegularExpressions;

namespace Logonaut.Filters
{
    /// <summary>
    /// A filter that checks whether a log line matches a specified regular expression.
    /// </summary>
    public class RegexFilter : FilterBase
    {
        private string _pattern;
        private Regex _regex;
        private bool _isCaseSensitive;

        public string Pattern
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

        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true; // Disabled filters are treated as neutral.
            
            if (line == null || _regex == null)
                return false;

            return _regex.IsMatch(line);
        }
    }
}
