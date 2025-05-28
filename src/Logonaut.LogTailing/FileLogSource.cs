using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Logonaut.Core; // For ILogSource

namespace Logonaut.LogTailing;

/// <summary>
/// An ILogSource implementation that reads initial lines from a file
/// and then tails it for new lines using LogTailer.
/// </summary>
public class FileLogSource : ILogSource
{
    private readonly Subject<string> _logLinesSubject = new Subject<string>();
    private LogTailer? _currentTailer;
    private IDisposable? _tailerSubscription;
    private string? _currentFilePath;
    private long _startPositionForTailing = 0; // Position after initial read
    private bool _isMonitoring = false;
    private readonly object _lock = new object();

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    public async Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentifier))
            throw new ArgumentException("File path cannot be null or empty.", nameof(sourceIdentifier));

        lock (_lock)
        {
            // Ensure any previous monitoring is stopped before preparing a new file
            StopMonitoringInternal();
            _currentFilePath = sourceIdentifier;
            _startPositionForTailing = 0; // Reset start position
        }

        long initialLineCount = 0;

        try
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> FileLogSource: Starting initial read for {sourceIdentifier}");

            // Perform initial read using FileStream for flexibility
            using (var fs = new FileStream(sourceIdentifier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs)) // Consider adding encoding options if needed
            {
                string? line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    addLineToDocumentCallback.Invoke(line);
                    initialLineCount++;
                }
                _startPositionForTailing = fs.Position; // Store position *after* reading
            }

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> FileLogSource: Finished initial read. {initialLineCount} lines read. Tailing start position: {_startPositionForTailing}.");
            return initialLineCount;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} !!! FileLogSource: Error during initial read for {sourceIdentifier}: {ex.Message}");
            // Ensure cleanup on error during preparation
            lock (_lock) { StopMonitoringInternal(); }
            // Re-throw for the caller (MainViewModel) to handle UI feedback
            throw new IOException($"Failed to read file '{sourceIdentifier}'. Reason: {ex.Message}", ex);
        }
    }

    public void StartMonitoring(Action? onLogFileResetDetected)
    {
        lock (_lock)
        {
            if (_isMonitoring)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> FileLogSource: Already monitoring '{_currentFilePath}'. StartMonitoring called again ignored.");
                return; // Already monitoring
            }
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                throw new InvalidOperationException("PrepareAndGetInitialLinesAsync must be called successfully before StartMonitoring.");
            }

            try
            {
                // --- Start Tailing for NEW Lines ---
                StopMonitoringInternal(); // Ensure previous tailer is disposed if any

                _currentTailer = new LogTailer(_currentFilePath, _startPositionForTailing, onLogFileResetDetected);

                _tailerSubscription = _currentTailer.LogLines.Subscribe(
                    _logLinesSubject.OnNext,    // Forward new lines
                    ex => {                     // Forward errors
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} !!! FileLogSource: Error received from LogTailer for '{_currentFilePath}': {ex.Message}. Forwarding.");
                        _logLinesSubject.OnError(ex);
                        StopMonitoringInternal(); // Stop monitoring on tailer error
                    },
                    () => {                     // Handle completion (optional)
                         Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> FileLogSource: LogTailer completed for '{_currentFilePath}'.");
                         // Optionally complete _logLinesSubject here if appropriate
                         // _logLinesSubject.OnCompleted();
                         StopMonitoringInternal(); // Stop monitoring on tailer completion
                    }
                );

                _currentTailer.StartMonitoring(); // Start the underlying tailer
                _isMonitoring = true;
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> FileLogSource: Started monitoring for new lines in '{_currentFilePath}' from position {_startPositionForTailing}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} !!! FileLogSource: Error starting LogTailer for '{_currentFilePath}': {ex.Message}");
                StopMonitoringInternal(); // Ensure cleanup on error during start
                 _logLinesSubject.OnError(new IOException($"Failed to start tailing file '{_currentFilePath}'. Reason: {ex.Message}", ex));
                // Don't re-throw here, let the OnError propagate
            }
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            StopMonitoringInternal();
        }
    }

    private void StopMonitoringInternal() // Internal version without lock for reuse
    {
        if (!_isMonitoring && _currentTailer == null) return; // Nothing to stop

        Debug.WriteLineIf(_isMonitoring || _currentTailer != null, $"{DateTime.Now:HH:mm:ss.fff} ---> FileLogSource: StopMonitoring called for '{_currentFilePath}'.");
        _tailerSubscription?.Dispose();
        _tailerSubscription = null;
        _currentTailer?.Dispose();
        _currentTailer = null;
        _isMonitoring = false;
        // DO NOT reset _currentFilePath or _startPositionForTailing here,
        // PrepareAndGetInitialLinesAsync handles that for the next file.
        // DO NOT complete _logLinesSubject here, let Dispose handle final completion.
    }

    public void Dispose()
    {
        lock (_lock)
        {
            StopMonitoringInternal();
            _logLinesSubject?.OnCompleted(); // Complete the subject on dispose
            _logLinesSubject?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}