namespace Logonaut.Filters
{
    /// <summary>
    /// A filter that negates the result of an inner filter.
    /// </summary>
    public class NegationFilter : FilterBase
    {
        public IFilter? InnerFilter { get; set; }

        public NegationFilter(IFilter innerFilter)
        {
            InnerFilter = innerFilter ?? throw new ArgumentNullException(nameof(innerFilter));
        }

        public override bool IsMatch(string line)
        {
            if (!Enabled)
                return true;

            if (InnerFilter == null)
                return true;
                
            return !InnerFilter.IsMatch(line);
        }

        public override string DisplayText => "¬";
    }
}
