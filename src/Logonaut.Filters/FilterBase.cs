using System;
using System.Collections.Generic;
using System.Linq;

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
    }

    /// <summary>
    /// Base class for filters that implements the Enabled property.
    /// </summary>
    public abstract class FilterBase : IFilter
    {
        public bool Enabled { get; set; } = true;

        public abstract bool IsMatch(string line);
    }

    /// <summary>
    /// Abstract base class for composite filters that combine multiple sub-filters.
    /// </summary>
    public abstract class CompositeFilterBase : FilterBase
    {
        protected readonly List<IFilter> Filters = new List<IFilter>();

        /// <summary>
        /// Adds a sub-filter to the composite.
        /// </summary>
        /// <param name="filter">The filter to add.</param>
        public void Add(IFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            Filters.Add(filter);
        }

        /// <summary>
        /// Removes a sub-filter from the composite.
        /// </summary>
        /// <param name="filter">The filter to remove.</param>
        /// <returns>True if the filter was removed; otherwise, false.</returns>
        public bool Remove(IFilter filter)
        {
            return Filters.Remove(filter);
        }
    }
}
