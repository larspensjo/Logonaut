using System;
using System.Reactive;

namespace Logonaut.Core // Or other appropriate namespace
{
    /// <summary>
    /// Interface for a service that manages log file tailing.
    /// </summary>
    public interface ILogTailerService : IDisposable
    {
        /// <summary>
        /// Gets an observable sequence of new log lines from the currently tailed file.
        /// Consumers should subscribe to this to receive updates.
        /// </summary>
        IObservable<string> LogLines { get; }
        IObservable<Unit> InitialReadComplete { get; }

        /// <summary>
        /// Starts tailing a new log file or changes the currently tailed file.
        /// Disposes any previous tailer.
        /// </summary>
        /// <param name="filePath">The full path to the log file to tail.</param>
        /// <exception cref="System.ArgumentException">Thrown if filePath is null or empty.</exception>
        /// <exception cref="System.IO.FileNotFoundException">Thrown if the file does not exist.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the directory path is invalid.</exception>
        /// <exception cref="System.Exception">Other exceptions related to file access or watcher setup.</exception>
        Task ChangeFileAsync(string filePath);

        /// <summary>
        /// Stops tailing the current file and releases resources.
        /// </summary>
        void StopTailing();
    }
}