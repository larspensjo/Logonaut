// In Logonaut.LogTailing (or potentially Logonaut.Core/Infrastructure)
using System;
using System.Reactive; // For Unit
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Logonaut.Core; // Where ILogTailerService is defined

namespace Logonaut.LogTailing
{
    public class LogTailerService : ILogTailerService
    {
        private LogTailer? _currentTailer;
        private IDisposable? _tailerSubscription;
        private readonly Subject<string> _logLinesSubject = new Subject<string>();
        private readonly ReplaySubject<Unit> _initialReadCompleteRelay = new ReplaySubject<Unit>(1);
        private IDisposable? _initialReadSubscription;
        private readonly object _lock = new object(); // For thread safety

        public IObservable<string> LogLines => _logLinesSubject.AsObservable();
        public IObservable<Unit> InitialReadComplete => _initialReadCompleteRelay.AsObservable(); // <<< Expose relay

        // Constructor (could be public if needed elsewhere, or internal if only used via DI)
        public LogTailerService() { }

        public async Task ChangeFileAsync(string filePath)
        {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            // Temporary references for disposal after background task
            LogTailer? oldTailer = null;
            IDisposable? oldTailerSubscription = null;
            IDisposable? oldInitialReadSubscription = null;

            LogTailer newTailer; // Declare outside lambda

            await Task.Run(() =>
            {
                // Inside lock for state mutation
                lock (_lock)
                {
                    // Capture old instances for disposal outside lock if needed,
                    // but better to dispose them here if possible.
                    oldTailer = _currentTailer;
                    oldTailerSubscription = _tailerSubscription;
                    oldInitialReadSubscription = _initialReadSubscription; // Capture old read subscription

                    _currentTailer = null; // Clear references before creating new
                    _tailerSubscription = null;
                    _initialReadSubscription = null;


                    // --- Reset the Relay Subject ---
                    // We create a new one or signal reset somehow. Simpler: let consumers re-subscribe?
                    // Or, maybe better: Don't expose the internal subject directly.
                    // Let's stick with the ReplaySubject(1) approach for now. The next OnNext will replace the old value.


                    // --- Create and Start New Tailer ---
                    newTailer = new LogTailer(filePath); // Assign to outer variable

                    // Subscribe to the new tailer's lines
                    _tailerSubscription = newTailer.LogLines.Subscribe(
                        _logLinesSubject.OnNext,
                        _logLinesSubject.OnError
                    );

                    // --- Subscribe to the NEW tailer's completion signal ---
                    _initialReadSubscription = newTailer.InitialReadComplete.Subscribe(
                        _ => _initialReadCompleteRelay.OnNext(Unit.Default), // Forward signal
                        ex => _initialReadCompleteRelay.OnError(ex)         // Forward error
                        // OnCompleted is handled by ReplaySubject
                    );

                    _currentTailer = newTailer; // Assign new tailer to main field

                    newTailer.Start(); // Start monitoring AFTER subscriptions are set up
                } // End lock

                // Dispose old instances AFTER releasing lock and finishing background setup
                oldInitialReadSubscription?.Dispose();
                oldTailerSubscription?.Dispose();
                oldTailer?.Dispose();

            }).ConfigureAwait(false);
        }

        public void StopTailing()
        {
            lock (_lock)
            {
                StopInternal();
                // Optionally signal completion if the subject is still active,
                // though usually Dispose handles this.
                // _logLinesSubject.OnCompleted();
            }
        }

        private void StopInternal()
        {
            // Called within lock
            _initialReadSubscription?.Dispose(); // <<< Dispose read subscription
            _initialReadSubscription = null;

            _tailerSubscription?.Dispose();
            _tailerSubscription = null;

            _currentTailer?.Dispose();
            _currentTailer = null;

            // Optionally reset the relay subject if needed when stopping explicitly
            // _initialReadCompleteRelay.OnError(new OperationCanceledException("Tailing stopped."));
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;
            lock(_lock)
            {
                StopInternal(); // Calls dispose on subscriptions and tailer
                _logLinesSubject?.OnCompleted();
                _logLinesSubject?.Dispose();
                _initialReadCompleteRelay?.OnCompleted(); // Complete relay
                _initialReadCompleteRelay?.Dispose();
            }
        }
    }
}