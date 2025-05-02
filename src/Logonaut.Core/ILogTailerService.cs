using System;
using System.Reactive;
using Logonaut.Common;

namespace Logonaut.Core;

/*
* Defines the contract for a service managing the continuous monitoring ("tailing") of a log file.
*
* Purpose:
* To abstract the specifics of watching a file for changes and reading newly appended lines,
* providing these updates reactively.
*
* Role & Lifecycle:
* Manages the lifecycle for monitoring a *single* file at a time. `ChangeFileAsync`
* handles switching to a new file, performing an initial read (via callback), and starting
* the tailing process. New lines are delivered via the `LogLines` observable. `StopTailing`
* halts the monitoring.
*
* Benefits:
* - Decouples file tailing logic from consumers (like ViewModels).
* - Allows different tailing implementations or strategies.
* - Simplifies testing by enabling mock tailing services.
*
* Implementations handle file system interactions and resource management (IDisposable).
*/
public interface ILogTailerService : IDisposable
{
    /// <summary>
    /// Gets an observable sequence of new log lines from the currently tailed file.
    /// Consumers should subscribe to this to receive updates.
    /// </summary>
    IObservable<string> LogLines { get; }

    /// <summary>
    /// Starts tailing a new log file or changes the currently tailed file.
    /// Disposes any previous tailer.
    Task<long> ChangeFileAsync(string filePath, Action<string> addLineToDocumentCallback);

    /// <summary>
    /// Stops tailing the current file and releases resources.
    /// </summary>
    void StopTailing();
}
