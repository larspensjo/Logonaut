using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Subjects;
using System.Reactive.Linq; // For AsObservable
using System.Threading.Tasks;
using Logonaut.Core; // For ILogSource

namespace Logonaut.TestUtils;

public class MockLogSource : ILogSource
{
    private readonly Subject<string> _logLinesSubject = new Subject<string>();
    private bool _isMonitoring = false;
    private bool _isPrepared = false;

    public bool IsDisposed { get; private set; } = false;
    public bool IsMonitoring => _isMonitoring;
    public bool IsPrepared => _isPrepared;
    public string? PreparedSourceIdentifier { get; private set; }
    public int StartMonitoringCallCount { get; private set; } = 0;
    public int StopMonitoringCallCount { get; private set; } = 0;

    /// <summary>
    /// Set this list *before* calling PrepareAndGetInitialLinesAsync
    /// to simulate the initial content of the source.
    /// </summary>
    public List<string> LinesForInitialRead { get; set; } = new List<string>();

    // --- ILogSource Implementation ---

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (string.IsNullOrEmpty(sourceIdentifier)) throw new ArgumentNullException(nameof(sourceIdentifier));

        // Simulate failure if needed for testing
        if (sourceIdentifier == "FAIL_PREPARE")
        {
                throw new IOException("Mock Prepare Failed");
        }

        PreparedSourceIdentifier = sourceIdentifier;
        _isPrepared = false; // Reset prepared state
        StopMonitoring();    // Stop any previous monitoring

        long count = 0;
        Debug.WriteLine($"---> MockLogSource: PrepareAndGetInitialLinesAsync started for '{sourceIdentifier}'. Reading {LinesForInitialRead.Count} initial lines.");
        foreach (var line in LinesForInitialRead)
        {
            addLineToDocumentCallback(line);
            count++;
        }
        Debug.WriteLine($"---> MockLogSource: PrepareAndGetInitialLinesAsync finished for '{sourceIdentifier}'. Read {count} lines.");
        _isPrepared = true; // Mark as prepared AFTER reading lines
        return Task.FromResult(count);
    }

    public void StartMonitoring()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (!_isPrepared) throw new InvalidOperationException("PrepareAndGetInitialLinesAsync must be called successfully before StartMonitoring.");

        if (!_isMonitoring)
        {
            StartMonitoringCallCount++;
            _isMonitoring = true;
            Debug.WriteLine($"---> MockLogSource: StartMonitoring called for '{PreparedSourceIdentifier}'. Now monitoring.");
        }
        else
        {
            Debug.WriteLine($"---> MockLogSource: StartMonitoring called for '{PreparedSourceIdentifier}' but already monitoring.");
        }
    }

    public void StopMonitoring()
    {
        if (IsDisposed) return;
        if (_isMonitoring)
        {
            StopMonitoringCallCount++;
            _isMonitoring = false;
                Debug.WriteLine($"---> MockLogSource: StopMonitoring called for '{PreparedSourceIdentifier}'. Monitoring stopped.");
        }
    }

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

    // --- IDisposable ---

    public void Dispose()
    {
        if (IsDisposed) return;
        Debug.WriteLine($"---> MockLogSource: Dispose called for '{PreparedSourceIdentifier}'.");
        IsDisposed = true;
        StopMonitoring(); // Ensure monitoring stops on dispose
        _logLinesSubject.OnCompleted(); // Signal completion
        _logLinesSubject.Dispose();     // Dispose subject
        GC.SuppressFinalize(this);
    }
}
