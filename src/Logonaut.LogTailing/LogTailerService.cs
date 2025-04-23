// In Logonaut.LogTailing (or potentially Logonaut.Core/Infrastructure)
using System;
using System.Reactive; // For Unit
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Diagnostics; // For Debug
using Logonaut.Core; // Where ILogTailerService is defined
using Logonaut.Common;

namespace Logonaut.LogTailing;
public class LogTailerService : ILogTailerService
{
    private LogTailer? _currentTailer;
    private IDisposable? _tailerSubscription;
    private readonly Subject<string> _logLinesSubject = new Subject<string>();
    private readonly object _lock = new object(); // For thread safety around tailer creation/disposal

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    public async Task<long> ChangeFileAsync(string filePath, Action<string> addLineToDocumentCallback)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        long initialLineCount = 0;
        long fileLength = 0;

        // --- Stop Previous Tailing ---
        StopInternal(); // Dispose existing tailer and subscriptions

        try
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> LogTailerService: Starting initial read for {filePath}");

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs)) // Consider adding encoding options if needed
            {
                string? line;
                // Use ConfigureAwait(false) to avoid capturing context unless needed
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    addLineToDocumentCallback.Invoke(line); // <<< USE CALLBACK
                    initialLineCount++;
                }
                fileLength = fs.Position; // Get the length AFTER reading
            }

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> LogTailerService: Finished initial read. {initialLineCount} lines read. File length: {fileLength}.");

            // --- Start Tailing for NEW Lines ---
            lock (_lock) // Protect creation/subscription of new tailer
            {
                // Pass starting position
                _currentTailer = new LogTailer(filePath, startPosition: fileLength);

                _tailerSubscription = _currentTailer.LogLines.Subscribe(
                    _logLinesSubject.OnNext,
                    _logLinesSubject.OnError
                );

                // Start monitoring AFTER subscriptions are set up
                _currentTailer.StartMonitoring();
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> LogTailerService: Started monitoring for new lines.");
            }

            return initialLineCount; // Return the count read during initial phase
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} !!! LogTailerService: Error during initial read or tailer start for {filePath}: {ex.Message}");
            StopInternal(); // Ensure cleanup on error
            // Re-throw for the caller (MainViewModel) to handle UI feedback
            throw new IOException($"Failed to read or start tailing file '{filePath}'. Reason: {ex.Message}", ex);
        }
    }

    public void StopTailing()
    {
        StopInternal();
    }

    private void StopInternal()
    {
        lock (_lock)
        {
            _tailerSubscription?.Dispose();
            _tailerSubscription = null;
            _currentTailer?.Dispose();
            _currentTailer = null;
            Debug.WriteLineIf(_currentTailer != null || _tailerSubscription != null, $"{DateTime.Now:HH:mm:ss.fff} ---> LogTailerService: Tailing stopped.");
        }
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
            StopInternal();
            _logLinesSubject?.OnCompleted(); // Complete the subject on dispose
            _logLinesSubject?.Dispose();
        }
    }
}
