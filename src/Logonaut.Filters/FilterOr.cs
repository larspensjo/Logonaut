using System;
using System.Collections.Generic;
using System.Linq;

namespace Logonaut.Filters
{
    /// <summary>
    /// A composite filter that matches a log line if any sub-filter matches (logical OR).
    /// </summary>
    public class OrFilter : CompositeFilter
    {
        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true;
                
            // If no filters, match nothing
            if (Filters.Count == 0)
                return false;
                
            // OR logic: at least one filter must match
            return Filters.Any(filter => filter.IsMatch(line));
        }
    }
}
