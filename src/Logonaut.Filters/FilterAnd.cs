using System;
using System.Collections.Generic;
using System.Linq;

namespace Logonaut.Filters
{
    /// <summary>
    /// A composite filter that only matches a log line if all sub-filters match (logical AND).
    /// </summary>
    public class AndFilter : CompositeFilter
    {
        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true;
                
            // If no filters, match everything
            if (Filters.Count == 0)
                return true;
                
            // AND logic: all sub filters must match
            // If any filter is disabled, it doesn't affect the match
            return Filters.All(filter => filter.Enabled && filter.IsMatch(line));
        }

        public override string DisplayText => "∧";

        public override string TypeText => "AndType";
    }
}
