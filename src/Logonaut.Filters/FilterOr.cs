using System;
using System.Collections.Generic;
using System.Linq;

namespace Logonaut.Filters
{
    /// <summary>
    /// A composite filter that matches a log line if any sub-filter matches (logical OR).
    /// </summary>
    public class OrFilter : CompositeFilterBase
    {
        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true;

            // If there are no sub-filters, treat it as matching nothing.
            if (Filters.Count == 0)
                return false;

            return Filters.Any(filter => filter.IsMatch(line));
        }
    }
}
