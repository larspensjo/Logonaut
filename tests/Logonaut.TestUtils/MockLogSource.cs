using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Logonaut.Core;

namespace Logonaut.TestUtils;

public class MockLogSource : ISimulatorLogSource
{
    private readonly Subject<string> _logLinesSubject = new Subject<string>();
    private bool _isMonitoring = false;
    private bool _isPrepared = false;

    public bool IsDisposed { get; private set; } = false;
    public bool IsPrepared => _isPrepared;
    public string? PreparedSourceIdentifier { get; private set; }
    public int StartCallCount { get; private set; } = 0;
    public int StopCallCount { get; private set; } = 0;
    public int RestartCallCount { get; private set; } = 0;
    public int UpdateRateCallCount { get; private set; } = 0;
    public int LastRateUpdate { get; private set; } = -1;

    public List<string> LinesForInitialRead { get; set; } = new List<string>();

    // --- ISimulatorLogSource Implementation ---
    public int LinesPerSecond { get; set; } = 10; // Add property
    public bool IsRunning => _isMonitoring;     // Implement property

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (string.IsNullOrEmpty(sourceIdentifier)) throw new ArgumentNullException(nameof(sourceIdentifier));

        PreparedSourceIdentifier = sourceIdentifier;
        _isPrepared = false; // Initial state before "successful" part of prepare

        // Simulate failure if needed for testing
        if (sourceIdentifier == "FAIL_PREPARE")
        {
            Debug.WriteLine($"---> MockLogSource: Simulating prepare failure for '{sourceIdentifier}'.");
            throw new IOException("Mock Prepare Failed");
        }

        // If not failing, proceed with mock preparation
        Stop(); // Call the mock's Stop method

        long count = 0;
        Debug.WriteLine($"---> MockLogSource: Prepare started for '{sourceIdentifier}'. Reading {LinesForInitialRead.Count} lines.");
        foreach (var line in LinesForInitialRead)
        {
            addLineToDocumentCallback(line);
            count++;
        }
        Debug.WriteLine($"---> MockLogSource: Prepare finished for '{sourceIdentifier}'. Read {count} lines.");
        _isPrepared = true;
        return Task.FromResult(count);
    }

    // Explicit ILogSource calls delegate to ISimulatorLogSource methods
    void ILogSource.StartMonitoring() => Start();
    void ILogSource.StopMonitoring() => Stop();

    public void Start()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (!_isPrepared) throw new InvalidOperationException("PrepareAndGetInitialLinesAsync must be called successfully before Start.");
        if (!_isMonitoring)
        {
            StartCallCount++;
            _isMonitoring = true;
            Debug.WriteLine($"---> MockLogSource: Start called for '{PreparedSourceIdentifier}'. Now monitoring.");
        } else {
             Debug.WriteLine($"---> MockLogSource: Start called for '{PreparedSourceIdentifier}' but already monitoring.");
        }
    }

    public void Stop()
    {
        if (IsDisposed) return;
        if (_isMonitoring)
        {
            StopCallCount++;
            _isMonitoring = false;
            Debug.WriteLine($"---> MockLogSource: Stop called for '{PreparedSourceIdentifier}'. Monitoring stopped.");
        }
    }

    public void Restart()
    {
         if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
         RestartCallCount++;
         Debug.WriteLine($"---> MockLogSource: Restart called for '{PreparedSourceIdentifier}'.");
         // Simulate basic Stop/Start behavior for the mock
         Stop();
         Start();
    }

    public void UpdateRate(int newLinesPerSecond)
    {
         if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
         UpdateRateCallCount++;
         LastRateUpdate = newLinesPerSecond;
         LinesPerSecond = newLinesPerSecond; // Update internal state
         Debug.WriteLine($"---> MockLogSource: UpdateRate called ({newLinesPerSecond}) for '{PreparedSourceIdentifier}'.");
         // Mock doesn't need to restart timer, just record the call/value
    }
    // --- End ISimulatorLogSource Implementation ---


    // --- Simulation Methods for Tests ---
    /// <summary>
    /// Emits a line if monitoring is active. Call StartMonitoring() first.
    /// </summary>
    public void EmitLine(string line)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (!_isMonitoring)
        {
            Debug.WriteLine($"---> MockLogSource: EmitLine called for '{line}' but not monitoring. Ignored.");
            // Optionally throw or handle differently if emitting when not monitoring is an error condition in tests
            // throw new InvalidOperationException("Cannot emit lines when not monitoring.");
            return;
        }
        Debug.WriteLine($"---> MockLogSource: Emitting line: '{line}'");
        _logLinesSubject.OnNext(line);
    }

    /// <summary>
    /// Emits an error if not disposed.
    /// </summary>
    public void EmitError(Exception ex)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        Debug.WriteLine($"---> MockLogSource: Emitting error: {ex.Message}");
        _logLinesSubject.OnError(ex);
        // Optionally stop monitoring on error? Depends on desired simulation
        // StopMonitoring();
    }

    /// <summary>
    /// Emits completion if not disposed.
    /// </summary>
    public void EmitCompletion()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        Debug.WriteLine($"---> MockLogSource: Emitting completion.");
        _logLinesSubject.OnCompleted();
        // Stop monitoring on completion?
        // StopMonitoring();
    }

    public Task GenerateBurstAsync(int lineCount)
    {
        throw new NotImplementedException("Burst generation not implemented in MockLogSource.");
    }
    public int ErrorFrequency
    {
        get;
        set;
    }

    // --- IDisposable ---

    public void Dispose()
    {
        if (IsDisposed) return;
        Debug.WriteLine($"---> MockLogSource: Dispose called for '{PreparedSourceIdentifier}'.");
        IsDisposed = true;
        Stop(); // Ensure monitoring stops on dispose
        _logLinesSubject.OnCompleted(); // Signal completion
        _logLinesSubject.Dispose();     // Dispose subject
        GC.SuppressFinalize(this);
    }
}
