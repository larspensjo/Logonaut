using System;
using System.Collections.Generic;
using System.Linq;

namespace Logonaut.Filters
{
    /// <summary>
    /// A filter that checks whether a log line contains a specified substring.
    /// </summary>
    public class SubstringFilter : FilterBase
    {
        public string Substring { get; set; }

        public override bool IsEditable => true;

        public SubstringFilter(string substring)
        {
            Substring = substring;
        }

        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true; // Disabled filters are treated as neutral.
            if (line == null || Substring == null)
                return false;

            return line.Contains(Substring);
        }
    }
}
