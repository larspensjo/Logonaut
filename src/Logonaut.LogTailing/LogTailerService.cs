// In Logonaut.LogTailing (or potentially Logonaut.Core/Infrastructure)
using System;
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
        private readonly object _lock = new object(); // For thread safety

        public IObservable<string> LogLines => _logLinesSubject.AsObservable();

        // Constructor (could be public if needed elsewhere, or internal if only used via DI)
        public LogTailerService() { }

        public void ChangeFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

             // We still need to check existence here before creating LogTailer
             // LogTailer constructor itself throws FileNotFoundException
             // if (!System.IO.File.Exists(filePath))
             //    throw new System.IO.FileNotFoundException("Log file not found.", filePath);

            lock (_lock)
            {
                // Stop and dispose previous tailer and subscription
                StopInternal();

                // Create and start a new tailer
                _currentTailer = new LogTailer(filePath); // Can throw exceptions

                // Subscribe to the new tailer's lines and forward them
                _tailerSubscription = _currentTailer.LogLines.Subscribe(
                    line => _logLinesSubject.OnNext(line), // Forward line
                    ex => _logLinesSubject.OnError(ex),    // Forward error
                    () => { /* Optional: Handle completion? Usually tailers don't complete unless disposed. */ }
                );

                _currentTailer.Start(); // Start monitoring
            }
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
            _tailerSubscription?.Dispose();
            _tailerSubscription = null;

            _currentTailer?.Dispose();
            _currentTailer = null;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock(_lock)
                {
                    StopInternal();
                    _logLinesSubject?.OnCompleted(); // Signal completion
                    _logLinesSubject?.Dispose();
                }
            }
        }
    }
}