using System;
using System.Threading.Tasks;

namespace Logonaut.Core;

/// <summary>
/// Represents a source of log lines, providing both initial content and ongoing updates.
/// </summary>
public interface ILogSource : IDisposable
{
    /// <summary>
    /// Gets an observable sequence of *new* log lines emitted after StartMonitoring() is called.
    /// This stream should typically complete or error out if the source becomes permanently unavailable.
    /// </summary>
    IObservable<string> LogLines { get; }

    /// <summary>
    /// Prepares the log source (e.g., opens a file, connects to a stream) and
    /// reads any initial/existing log lines, invoking the callback for each line read.
    /// This should be called *before* StartMonitoring.
    /// </summary>
    /// <param name="sourceIdentifier">An identifier for the source (e.g., file path, URL).</param>
    /// <param name="addLineToDocumentCallback">Action invoked for each initial line read.</param>
    /// <returns>A task completing with the number of initial lines read.</returns>
    Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback);

    /// <summary>
    /// Starts monitoring the source for *new* lines and emitting them via the LogLines observable.
    /// Should only be called after PrepareAndGetInitialLinesAsync has completed successfully.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops monitoring the source for new lines and releases associated resources for monitoring.
    /// Does not necessarily dispose the entire object, allowing for potential restart.
    /// </summary>
    void StopMonitoring();
}
