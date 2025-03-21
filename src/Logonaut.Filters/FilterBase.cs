using Newtonsoft.Json;

namespace Logonaut.Filters
{
    /// <summary>
    /// Interface representing a filter that determines whether a log line matches certain criteria.
    /// </summary>
    public interface IFilter
    {
        /// <summary>
        /// Evaluates if the provided log line meets the filter criteria.
        /// </summary>
        /// <param name="line">The log line to evaluate.</param>
        /// <returns>True if the line matches the filter criteria; otherwise, false.</returns>
        bool IsMatch(string line);

        /// <summary>
        /// Indicates whether the filter is active. When disabled, the filter is considered neutral.
        /// </summary>
        bool Enabled { get; set; }

        bool IsEditable { get; }
    }

    /// <summary>
    /// Base class for filters that implements the Enabled property.
    /// </summary>
    public abstract class FilterBase : IFilter
    {
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Indicates whether this filter can be edited by the user.
        /// </summary>
        public virtual bool IsEditable => false;

        public abstract bool IsMatch(string line);
    }

    /// <summary>
    /// Abstract base class for composite filters that combine multiple sub-filters.
    /// </summary>
    public abstract class CompositeFilterBase : FilterBase
    {
        // Use a private list, but expose it as a public read‑only property.
        protected readonly List<IFilter> Filters = new List<IFilter>();

        [JsonProperty] // Ensure these get serialized.
        public List<IFilter> SubFilters => Filters;

        public void Add(IFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            Filters.Add(filter);
        }

        public bool Remove(IFilter filter)
        {
            return Filters.Remove(filter);
        }
    }

    // A neutral filter that always returns true.
    public class TrueFilter : IFilter
    {
        public bool IsEditable => false;
        public bool Enabled { get; set; } = true;
        public bool IsMatch(string line) => true;
    }
}
