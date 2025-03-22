namespace Logonaut.Filters
{
    /// <summary>
    /// A composite filter that matches a log line if none sub-filter matches.
    /// </summary>
    public class NorFilter : CompositeFilter
    {
        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true;
                
            // If no filters, match nothing
            if (Filters.Count == 0)
                return true;
                
            // OR logic: at least one filter must match
            return Filters.All(filter => !filter.IsMatch(line));
        }
        
        public override string DisplayText => "¬∨";

        public override string TypeText => "NorType";
    }
}
