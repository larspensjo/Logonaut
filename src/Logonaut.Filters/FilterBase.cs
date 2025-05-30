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

        /// <summary>
        /// Indicates whether this filter can be edited by the user.
        /// </summary>
        bool IsEditable { get; }

        /// <summary>
        /// The text displayed in .
        /// </summary>
        // TODO: Should instead use ToString()
        string DisplayText { get; }

        /// <summary>
        /// Get the filter as a string. This is used by FilterTemplates.xaml
        /// </summary>
        string TypeText { get; }

        /// <summary>
        /// The value of the filter. Only used by some filters.
        /// </summary>
        string Value { get; set; }

        /// <summary>
        /// Gets or sets the resource key prefix for the highlight color (e.g., "FilterHighlight.Red").
        /// The actual brushes will be resolved using ".Background" and ".Foreground" suffixes.
        /// </summary>
        string HighlightColorKey { get; set; }
    }

    /// <summary>
    /// Optional base class providing typical common filter functionality for filters without editable values.
    /// </summary>
    // TODO: FilterBase should only be used by CompositeFilter. Merge them into one class.
    public abstract class FilterBase : IFilter
    {
        public bool Enabled { get; set; } = true;
        
        public virtual bool IsEditable => false;
        
        public abstract bool IsMatch(string line);

        public abstract string DisplayText { get; }

        public abstract string TypeText { get; }

        private string _value = string.Empty;

        [JsonProperty] public virtual string Value
        {
            get => _value;
            set => _value = value;
        }

        // Default highlight color key for filters that don't override
        [JsonProperty] public virtual string HighlightColorKey { get; set; } = "FilterHighlight.Default";
    }

    /// <summary>
    /// Abstract base class for composite filters that combine multiple sub-filters.
    /// </summary>
    public abstract class CompositeFilter : FilterBase
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
    public class TrueFilter : FilterBase
    {
        public override bool IsMatch(string line) => true;

        public override string DisplayText => "TRUE";

        public override string TypeText => "TRUE";
    }
}
