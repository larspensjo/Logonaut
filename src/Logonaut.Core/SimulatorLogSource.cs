using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Logonaut.Core;

public class SimulatorLogSource : ISimulatorLogSource
{
    private readonly Subject<string> _logLinesSubject = new Subject<string>();
    private Timer? _timer;
    private bool _isRunning = false;    private long _lineCounter = 0;
    private int _errorFrequency = 100; // Default: 1 error every 100 lines
    private readonly string[] _logLevels = { "INFO", "WARN", "DEBUG", "TRACE" };
    private readonly Random _random = new();
    private readonly object _lock = new object();

    private int _linesPerSecond = 10;
    public int LinesPerSecond
    {
        get => _linesPerSecond;
        set => UpdateRate(value); // Use UpdateRate logic for consistency
    }

    public bool IsRunning => _isRunning; // Implement IsRunning

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    public SimulatorLogSource() { }

    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback)
    {
        lock(_lock)
        {
            Debug.WriteLine($"---> SimulatorLogSource: Prepare called (Identifier: '{sourceIdentifier}').");
            StopInternal(); // Ensure stopped before prepare
            _lineCounter = 0;
            // No initial lines to read for simulator
            return Task.FromResult(0L);
        }
    }

    // Explicitly implement ILogSource methods by calling ISimulatorLogSource methods
    void ILogSource.StartMonitoring(Action? onLogFileResetDetected) => Start();
    void ILogSource.StopMonitoring() => Stop();

    #region ISimulatorLogSource Methods
    public void Start()
    {
         lock (_lock)
         {
            if (_isRunning) return; // Already running

            Debug.WriteLine($"---> SimulatorLogSource: Starting (Rate: {LinesPerSecond} LPS).");
            if (_linesPerSecond <= 0) return; // Don't start if rate is non-positive

            int intervalMs = Math.Max(1, 1000 / _linesPerSecond);
            _timer?.Dispose(); // Dispose old timer just in case
            _timer = new Timer(
                GenerateLogLineCallback,
                null,
                TimeSpan.FromMilliseconds(intervalMs), // Start after first interval
                TimeSpan.FromMilliseconds(intervalMs)  // Repeat interval
            );
            _isRunning = true;
         }
    }

    public void Stop()
    {
         lock (_lock)
         {
             StopInternal();
         }
    }

    public void Restart()
    {
        lock (_lock)
        {
            Debug.WriteLine("---> SimulatorLogSource: Restart requested.");
            StopInternal();
            _lineCounter = 0;
            Start();
        }
    }

    public void UpdateRate(int newLinesPerSecond)
    {
        lock (_lock)
        {
            int newRate = Math.Max(0, newLinesPerSecond);
            if (newRate == _linesPerSecond) return; // No change needed

            bool wasRunning = _isRunning; // Capture state *before* changing anything
            _linesPerSecond = newRate; // Update the internal rate field
            Debug.WriteLine($"---> SimulatorLogSource: Rate updated to {LinesPerSecond} LPS.");

            // --- New Decision Logic ---
            if (_linesPerSecond > 0) // Target state is "running" (rate > 0)
            {
                if (wasRunning) {
                    StopInternal(); // Stop timer, sets _isRunning = false temporarily
                    Start(); // Start timer with new _linesPerSecond, sets _isRunning = true
                    Debug.WriteLine($"---> SimulatorLogSource: Adjusted running timer interval.");
                } else { // wasRunning was false
                    Start(); // Start timer, sets _isRunning = true
                    Debug.WriteLine($"---> SimulatorLogSource: Starting timer as rate increased from 0.");
                }
            } else { // Target state is "stopped" (rate is 0)
                if (wasRunning) {
                    StopInternal(); // Stop timer, sets _isRunning = false
                    Debug.WriteLine($"---> SimulatorLogSource: Rate set to 0, simulation paused (timer stopped).");
                } // else: // wasRunning was false - already stopped, do nothing.
            }
        }
    }

    public Task GenerateBurstAsync(int lineCount)
    {
        if (lineCount <= 0) return Task.CompletedTask;

        Debug.WriteLine($"---> SimulatorLogSource: Starting burst of {lineCount} lines.");

        // Run the generation potentially off the main thread if it's very large,
        // but pushing to the subject should be okay from any thread.
        // Task.Run is simple for background execution.
        return Task.Run(() =>
        {
            try
            {
                // Use a local counter for the burst itself for clarity in messages
                for (int i = 0; i < lineCount; i++)
                {
                    long globalLineNum = Interlocked.Increment(ref _lineCounter); // Still use global counter
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string level = _logLevels[_random.Next(_logLevels.Length)];
                    // Pass the burst index 'i' or the global line number to the message generator
                    string message = GenerateRandomMessage(globalLineNum);
                    string logLine = $"{timestamp} [{level}] {message}";

                    // Push to the subject - the ReactiveFilteredLogStream will buffer this
                    _logLinesSubject.OnNext(logLine);
                }
                Debug.WriteLine($"---> SimulatorLogSource: Finished burst of {lineCount} lines.");
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"!!! SimulatorLogSource: Error during burst generation: {ex.Message}");
                 _logLinesSubject.OnError(ex); // Propagate error
                 // Re-throw or handle as appropriate
                 throw;
            }
        });
    }

    public int ErrorFrequency
    {
        get => _errorFrequency;
        set
        {
            lock (_lock) // Protect access if set externally while running
            {
                _errorFrequency = Math.Max(1, value); // Ensure frequency is at least 1
                Debug.WriteLine($"---> SimulatorLogSource: Error Frequency set to 1 in {_errorFrequency}");
            }
        }
    }

    #endregion // --- End ISimulatorLogSource Methods ---

    #region Internal & Private Methods
    private void StopInternal()
    {
        if (!_isRunning && _timer == null) return;
        Debug.WriteLineIf(_isRunning || _timer != null, $"---> SimulatorLogSource: Stopping timer.");
        _timer?.Dispose();
        _timer = null;
        _isRunning = false;
    }

    private void GenerateLogLineCallback(object? state)
    {
        if (!_isRunning || _timer == null) return;

        long currentLine = Interlocked.Increment(ref _lineCounter);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        string level;
        string message;
        int currentErrorFrequency; // Read the value safely inside the callback context
        lock (_lock)
        {
            currentErrorFrequency = _errorFrequency;
        }

        // --- Determine Level based on Frequency ---
        // Check if this line number is a multiple of the frequency
        if (currentLine > 0 && currentErrorFrequency > 0 && (currentLine % currentErrorFrequency == 0))
        {
            level = "ERROR";
            // Generate a specific error message or reuse random
            message = $"ERROR: Service API fail. Attempt {currentLine % 50 + 1}.";
        }
        else
        {
            // Original random logic
            level = _logLevels[_random.Next(_logLevels.Length)];
            message = GenerateRandomMessage(currentLine); // Original random message logic
        }

        string logLine = $"{timestamp} [{level}] {message}";
        _logLinesSubject.OnNext(logLine);
    }

    private string GenerateRandomMessage(long lineNumber)
    {
        int choice = _random.Next(20);
        return choice switch
        {
            0 => $"Processing request {lineNumber % 1000}",
            1 => $"User {(_random.Next(900) + 100)} logged in.",
            2 => $"DB query took {_random.Next(5, 150)}ms.",
            3 => $"Cache hit: data_block_{lineNumber % 50}",
            4 => $"Cache miss: user_prefs_{_random.Next(100)}",
            5 => "System health check OK.",
            6 => $"WARN: High CPU: {_random.Next(85, 99)}%",
            7 => "Config reloaded.",
            _ => $"Background task {lineNumber}.",
        };
    }

    #endregion // Internal & Private Methods

    public void Dispose()
    {
         lock (_lock)
         {
            Debug.WriteLine($"---> SimulatorLogSource: Dispose called.");
            StopInternal();
            _logLinesSubject?.OnCompleted();
            _logLinesSubject?.Dispose();
         }
        GC.SuppressFinalize(this);
    }
}
