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
        
        // AsyncSubject is suitable because it only emits the last value (or error) to subscribers after it completes. We only care about the event of completion.
        private readonly AsyncSubject<Unit> _initialReadCompleteSubject = new AsyncSubject<Unit>();
        private FileSystemWatcher? _watcher;
        private long _lastPosition;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Gets an observable sequence of new log lines.
        /// </summary>
        public IObservable<string> LogLines => _logLinesSubject;

        public IObservable<Unit> InitialReadComplete => _initialReadCompleteSubject.AsObservable();

        /// <summary>
        /// Initializes a new instance of the LogTailer class.
        /// </summary>
        /// <param name="filePath">The full path to the log file.</param>
        public LogTailer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            _filePath = filePath;
            if (!File.Exists(_filePath))
                throw new FileNotFoundException("Log file not found", _filePath);
        }

        /// <summary>
        /// Starts monitoring the log file.
        /// </summary>
        public void Start()
        {
            // Initialize the last read position to the current file length.
            _lastPosition = 0;

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

            // Note: No 'await' here, Start() should return quickly.
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
            bool initialReadSignalled = false; // Track if we've signalled completion
            try
            {
                // Open the file with shared read/write access to avoid locking issues.
                using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                // Seek to the last known position.
                stream.Seek(_lastPosition, SeekOrigin.Begin);

                using var reader = new StreamReader(stream);
                string? line;
                // Read lines until the end of the file.
                while (!reader.EndOfStream && !token.IsCancellationRequested)
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line != null)
                    {
                        _logLinesSubject.OnNext(line);
                    }
                }

                if (!token.IsCancellationRequested) // Only signal if not cancelled
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> LogTailer initial read pass finished. Read up to position {stream.Position}.");
                    _initialReadCompleteSubject.OnNext(Unit.Default); // Signal completion value
                    _initialReadCompleteSubject.OnCompleted(); // Complete the AsyncSubject
                    initialReadSignalled = true;
                }

                // Update the last read position.
                _lastPosition = stream.Position;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("{DateTime.Now:HH:mm:ss.fff}---> LogTailer read task canceled.");
                if (!initialReadSignalled) _initialReadCompleteSubject.OnError(new TaskCanceledException()); // Signal error if cancelled before completion
                _logLinesSubject.OnCompleted();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}!!! LogTailer Error during read: {ex.Message}");
                // Propagate exceptions through the observable stream.
                _logLinesSubject.OnError(ex);
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
