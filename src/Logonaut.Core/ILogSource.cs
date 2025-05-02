using System;
using System.Threading.Tasks;

namespace Logonaut.Core;

/*
 * Defines the contract for retrieving log data.
 *
 * Purpose:
 * Abstracts the origin of log entries (e.g., file, simulator, network)
 * so the rest of the application can process logs uniformly. It decouples
 * log acquisition from log filtering and display.
 *
 * Lifecycle:
 * Handles both reading existing log lines upon preparation (`PrepareAndGetInitialLinesAsync`)
 * and providing a reactive stream (`LogLines`) of new lines once monitoring starts (`StartMonitoring`).
 * `StopMonitoring` halts the live updates.
 *
 * Benefits:
 * - Enables supporting different log sources (files, simulations, etc.).
 * - Simplifies testing by allowing mock sources.
 * - Promotes separation of concerns within the application.
 *
 * Implementations manage source-specific details and resources, releasing them via IDisposable.
 */
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

/*
 * Defines the contract for creating specific log source instances (Factory pattern).
 *
 * Purpose:
 * Centralizes the creation of different ILogSource implementations (e.g., file, simulator),
 * decoupling consumers (like MainViewModel) from concrete source types.
 *
 * Role & Benefits:
 * - Enables Dependency Injection of log source creation logic.
 * - Simplifies swapping log sources (e.g., for testing with mocks).
 * - Makes adding new source types easier by extending the provider.
 *
 * Implementations return initialized log source objects.
 */
public interface ILogSourceProvider
{
    ILogSource CreateFileLogSource();
    ISimulatorLogSource CreateSimulatorLogSource(); // Return concrete type for control
}
