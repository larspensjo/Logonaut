
using System;
using Logonaut.Filters;

namespace Logonaut.Core;

/*
* Defines the contract for processing log data against filter rules.
*
* Purpose:
* Acts as the central engine for applying user-defined filters and context rules
* to the log data provided by an ILogSource. It produces a filtered view
* suitable for display.
*
* Role:
* Decouples the raw log source (ILogSource) and the filter definition logic (IFilter)
* from the UI layer that consumes the filtered results (e.g., MainViewModel). It manages
* the potentially complex task of evaluating lines against the filter tree and including
* surrounding context lines.
*
* Responsibilities:
* - Accessing raw log data (often in collaboration with LogDocument).
* - Applying an IFilter hierarchy to determine matching lines.
* - Calculating and retrieving context lines.
* - Providing reactive streams of filtered results (`FilteredUpdates`) and progress
*   information (`TotalLinesProcessed`).
* - Handling updates to filter configurations (`UpdateFilterSettings`).
* - Resetting state for new log sources (`Reset`).
* - Potentially performing filtering asynchronously to maintain UI responsiveness.
*
* Benefits:
* - Centralizes complex filtering logic.
* - Improves testability of the filtering core.
* - Enables separation of concerns.
*
* Implementations manage filter state and potentially background processing,
* releasing resources via IDisposable.
*/
public interface IReactiveFilteredLogStream : IDisposable
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
