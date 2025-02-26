using System;
using System.Collections.Generic;
using System.Linq;

namespace Logonaut.Filters
{
    /// <summary>
    /// A composite filter that only matches a log line if all sub-filters match (logical AND).
    /// </summary>
    public class AndFilter : CompositeFilterBase
    {
        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true;

            // If there are no sub-filters, treat it as matching everything.
            if (Filters.Count == 0)
                return true;

            return Filters.All(filter => filter.IsMatch(line));
        }
    }
}
