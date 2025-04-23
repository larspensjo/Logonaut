using System;
using System.Reactive;
using Logonaut.Common;

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

        /// <summary>
        /// Starts tailing a new log file or changes the currently tailed file.
        /// Disposes any previous tailer.
        Task<long> ChangeFileAsync(string filePath, Action<string> addLineToDocumentCallback);

        /// <summary>
        /// Stops tailing the current file and releases resources.
        /// </summary>
        void StopTailing();
    }
}