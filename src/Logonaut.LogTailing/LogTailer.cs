using System.Reactive.Subjects;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq; // Add this to use the Unit type

// This implementation uses a combination of FileSystemWatcher and asynchronous file reading to monitor a file for changes,
// and it leverages Reactive Extensions (Rx.NET) to expose an observable stream of new log lines.
namespace Logonaut.LogTailing
{
    /// <summary>
    /// Monitors a log file for new lines and provides them as an observable stream.
    /// </summary>
    public class LogTailer : IDisposable
    {
        private readonly string _filePath;
        private readonly Subject<string> _logLinesSubject = new Subject<string>();
        
        private FileSystemWatcher? _watcher;
        private long _lastPosition;
        private long _startPosition; // Position to start reading from
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Gets an observable sequence of new log lines.
        /// </summary>
        public IObservable<string> LogLines => _logLinesSubject;

        private readonly Action? _onLogFileResetDetected;

        /// <summary>
        /// Initializes a new instance of the LogTailer class.
        /// </summary>
        /// <param name="filePath">The full path to the log file.</param>

        public LogTailer(string filePath, long startPosition, Action? onLogFileResetDetected)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            _filePath = filePath;
            _startPosition = startPosition; // Store the starting position
            _onLogFileResetDetected = onLogFileResetDetected;
            if (!File.Exists(_filePath))
            throw new FileNotFoundException("Log file not found", _filePath);
        }

        /// <summary>
        /// Starts monitoring the log file.
        /// </summary>
        public void StartMonitoring()
        {
            _lastPosition = _startPosition; // Start reading from the specified position

            // Configure the file system watcher.
            var directory = Path.GetDirectoryName(_filePath);
            var fileName = Path.GetFileName(_filePath);
            if (directory == null)
                throw new InvalidOperationException("Directory path cannot be null.");

            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;

            // Initial check for any changes that occurred between initial read and watcher start
            // Use Task.Run for the initial check AND subsequent watcher triggers
            Task.Run(() => ReadNewLinesAsync(_cts.Token), _cts.Token);
        }

        /// <summary>
        /// Handles the Changed event of the FileSystemWatcher.
        /// </summary>
        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            await ReadNewLinesAsync(_cts.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Asynchronously reads any new lines appended to the log file.
        /// </summary>
        private async Task ReadNewLinesAsync(CancellationToken token)
        {
            // Read lines *beyond* _lastPosition
            try
            {
                using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < _lastPosition)
                {
                    _lastPosition = 0; // File was likely truncated, reset position
                    _onLogFileResetDetected?.Invoke();
                }
                stream.Seek(_lastPosition, SeekOrigin.Begin); // Seek to last known position

                using var reader = new StreamReader(stream);
                string? line;
                // Read lines until the end of the file.
                while (!reader.EndOfStream && !token.IsCancellationRequested)
                {
                    line = await reader.ReadLineAsync(token).ConfigureAwait(false); // Pass token
                    if (line != null)
                    {
                        _logLinesSubject.OnNext(line); // Emit NEW lines
                    }
                }
                _lastPosition = stream.Position; // Update position
            }
            catch (OperationCanceledException)
            {
                // This is expected during Dispose, just log informative message
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogTailer read task canceled for {_filePath}.");
                // DO NOT complete the subject here, Dispose handles that.
                // DO NOT signal initial read completion here.
            }
            catch (FileNotFoundException) // Handle case where file disappears between checks
            {
                 Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}!!! LogTailer Error: File not found during read attempt: {_filePath}");
                 _logLinesSubject.OnError(new FileNotFoundException($"Log file '{_filePath}' was not found or became inaccessible during tailing.", _filePath));
                 // Consider disposing the tailer as it can't continue
                 Dispose();
            }
             catch (IOException ioEx) // Handle other potential IO errors (e.g., network issues, locks)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}!!! LogTailer IO Error during read for {_filePath}: {ioEx.Message}");
                _logLinesSubject.OnError(ioEx);
                // Decide if this is fatal or if retrying is possible (more complex)
                // Dispose(); // Example: Treat IO errors as fatal for now
            }
            catch (Exception ex) // Catch-all for unexpected errors
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}!!! LogTailer Unexpected Error during read for {_filePath}: {ex.Message}");
                _logLinesSubject.OnError(ex); // Propagate errors
                // Dispose(); // Treat unexpected errors as fatal
            }
        }

        /// <summary>
        /// Disposes the LogTailer and stops file monitoring.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _watcher?.Dispose();
            _logLinesSubject?.OnCompleted();
            _logLinesSubject?.Dispose();
            _cts?.Dispose();
        }
    }
}
