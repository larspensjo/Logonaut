using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading; // Required for Timer
using System.Threading.Tasks;

namespace Logonaut.Core;

/// <summary>
/// An ILogSource implementation that simulates log generation in real-time.
/// </summary>
public class SimulatorLogSource : ILogSource
{
    private readonly Subject<string> _logLinesSubject = new Subject<string>();
    private Timer? _timer;
    private bool _isMonitoring = false;
    private bool _isDisposed = false;
    private long _lineCounter = 0;
    private int _linesPerSecond = 10; // Default generation rate
    private readonly string[] _logLevels = { "INFO", "WARN", "ERROR", "DEBUG", "TRACE" };
    private readonly Random _random = new Random();
    private readonly object _lock = new object(); // Lock for timer/state management

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    /// <summary>
    /// Initializes a new instance of the SimulatorLogSource.
    /// </summary>
    /// <param name="linesPerSecond">Approximate number of lines to generate per second.</param>
    public SimulatorLogSource(int linesPerSecond = 10)
    {
        if (linesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(linesPerSecond), "Lines per second must be positive.");
        _linesPerSecond = linesPerSecond;
    }

    /// <summary>
    /// Prepares the simulator. For the simulator, this does nothing significant
    /// other than validating the identifier and returning 0 initial lines.
    /// </summary>
    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SimulatorLogSource));
        // sourceIdentifier could potentially be used to load different simulation profiles later
        Debug.WriteLine($"---> SimulatorLogSource: Prepare called (Identifier: '{sourceIdentifier}'). No initial lines.");
        // Simulate readiness for StartMonitoring
        return Task.FromResult(0L);
    }

    /// <summary>
    /// Starts generating simulated log lines periodically.
    /// </summary>
    public void StartMonitoring()
    {
         lock (_lock)
         {
            if (_isDisposed) throw new ObjectDisposedException(nameof(SimulatorLogSource));
            if (_isMonitoring)
            {
                Debug.WriteLine($"---> SimulatorLogSource: Already monitoring. StartMonitoring called again ignored.");
                return; // Already monitoring
            }

            Debug.WriteLine($"---> SimulatorLogSource: Starting monitoring (generating ~{_linesPerSecond} lines/sec).");

            // Calculate interval: avoid division by zero and handle high rates
            int intervalMs = Math.Max(1, 1000 / _linesPerSecond); // At least 1ms interval

            // Create and start the timer
            // Timer callback executes on a ThreadPool thread
            _timer = new Timer(
                GenerateLogLineCallback,
                null,
                TimeSpan.Zero, // Start immediately
                TimeSpan.FromMilliseconds(intervalMs) // Repeat interval
            );

            _isMonitoring = true;
         }
    }

    /// <summary>
    /// Stops generating simulated log lines.
    /// </summary>
    public void StopMonitoring()
    {
         lock (_lock)
         {
             StopMonitoringInternal();
         }
    }

    // Internal version without lock for reuse in Dispose
    private void StopMonitoringInternal()
    {
        if (!_isMonitoring && _timer == null) return;

        Debug.WriteLineIf(_isMonitoring || _timer != null, $"---> SimulatorLogSource: StopMonitoring called.");
        _timer?.Dispose(); // Stop and release the timer
        _timer = null;
        _isMonitoring = false;
    }

    private void GenerateLogLineCallback(object? state)
    {
         // Prevent generating lines if stopped/disposed between timer tick and execution
         if (!_isMonitoring || _isDisposed || _timer == null) return;

        try
        {
            long currentLine = Interlocked.Increment(ref _lineCounter);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string level = _logLevels[_random.Next(_logLevels.Length)];
            string message = GenerateRandomMessage(currentLine);

            string logLine = $"{timestamp} [{level}] {message}";

            // Push to the subject (thread-safe)
            _logLinesSubject.OnNext(logLine);
        }
        catch (Exception ex) // Catch potential errors during generation/emission
        {
             Debug.WriteLine($"!!! SimulatorLogSource: Error in generation callback: {ex.Message}");
             // Optionally push error to subject? Depends if generation errors should stop the stream.
             // _logLinesSubject.OnError(ex);
             // StopMonitoring(); // Stop simulation on error?
        }
    }

    private string GenerateRandomMessage(long lineNumber)
    {
        int choice = _random.Next(10);
        return choice switch
        {
            0 => $"Processing request {lineNumber % 1000}",
            1 => $"User {(_random.Next(900) + 100)} logged in successfully.",
            2 => $"Database query took {_random.Next(5, 150)}ms.",
            3 => $"Cache hit for key: data_block_{lineNumber % 50}",
            4 => $"Cache miss for key: user_prefs_{_random.Next(100)}",
            5 => "System health check OK.",
            6 => $"WARN: High CPU usage detected: {_random.Next(85, 99)}%",
            7 => $"ERROR: Failed to connect to external service API. Attempt {lineNumber % 3 + 1}.",
            8 => "Configuration reloaded.",
            _ => $"Performing background task {lineNumber}.",
        };
    }

    public void Dispose()
    {
         lock (_lock)
         {
            if (_isDisposed) return;
            Debug.WriteLine($"---> SimulatorLogSource: Dispose called.");
            _isDisposed = true;
            StopMonitoringInternal(); // Stop timer
            _logLinesSubject?.OnCompleted(); // Complete the subject
            _logLinesSubject?.Dispose();
         }
        GC.SuppressFinalize(this);
    }
}
