
using System;
using Logonaut.Filters;

namespace Logonaut.Core
{
    /// <summary>
    /// Interface for a service that processes log lines against filters reactively.
    /// </summary>
    public interface ILogFilterProcessor : IDisposable
    {
        /// <summary>
        /// Gets an observable sequence of updates for the filtered log view.
        /// </summary>
        IObservable<FilteredUpdateBase> FilteredUpdates { get; }

        /// <summary>
        /// Gets an observable sequence representing the total number of lines
        /// processed from the source log.
        /// </summary>
        IObservable<long> TotalLinesProcessed { get; }

        /// <summary>
        /// Signals the processor that the filter configuration has changed.
        /// The processor will apply the new filter (potentially debounced).
        /// </summary>
        /// <param name="newFilter">The new filter tree to apply.</param>
        /// <param name="contextLines">The number of context lines to include.</param>
        void UpdateFilterSettings(IFilter newFilter, int contextLines);

        /// <summary>
        /// Clears the internal state, including the underlying LogDocument and line counter.
        /// Should be called when switching to a new log file.
        /// </summary>
        void Reset();
    }
}
