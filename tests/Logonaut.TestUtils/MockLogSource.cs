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
    private Subject<string> _logLinesSubject = new Subject<string>(); // Re-creatable
    private bool _isMonitoring = false;
    private bool _isPrepared = false;
    private Action? _fileResetCallbackFromStartMonitoring;

    public bool IsDisposed { get; private set; } = false; // Keep for interface/contract
    public bool IsActuallyDisposed { get; private set; } = false; // For mock's internal state

    public bool IsPrepared => _isPrepared;
    public string? PreparedSourceIdentifier { get; private set; }
    public int StartCallCount { get; private set; } = 0;
    public int StopCallCount { get; private set; } = 0;
    public int RestartCallCount { get; private set; } = 0;
    public int UpdateRateCallCount { get; private set; } = 0;
    public int LastRateUpdate { get; private set; } = -1;
    public int PrepareCallCount { get; private set; } = 0;

    public List<string> LinesForInitialRead { get; set; } = new List<string>();

    public int LinesPerSecond { get; set; } = 10;
    public bool IsRunning => _isMonitoring;

    public IObservable<string> LogLines => _logLinesSubject.AsObservable();

    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback)
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource), "MockLogSource is actually disposed and cannot be reused."); // More strict check
        if (string.IsNullOrEmpty(sourceIdentifier)) throw new ArgumentNullException(nameof(sourceIdentifier));

        PrepareCallCount++;
        PreparedSourceIdentifier = sourceIdentifier;
        _isPrepared = false;
        // Reset subject if it was completed from a previous "dispose"
        if (_logLinesSubject.IsDisposed || _logLinesSubject.HasObservers == false && PrepareCallCount > 1) // A bit heuristic
        {
            _logLinesSubject.Dispose(); // Dispose old one if exists
            _logLinesSubject = new Subject<string>(); // Create a new one for "revival"
        }


        if (sourceIdentifier == "FAIL_PREPARE")
        {
            Debug.WriteLine($"---> MockLogSource: Simulating prepare failure for '{sourceIdentifier}'.");
            throw new IOException("Mock Prepare Failed");
        }

        Stop();

        long count = 0;
        Debug.WriteLine($"---> MockLogSource: Prepare started for '{sourceIdentifier}'. Reading {LinesForInitialRead.Count} lines.");
        foreach (var line in LinesForInitialRead)
        {
            addLineToDocumentCallback(line);
            count++;
        }
        Debug.WriteLine($"---> MockLogSource: Prepare finished for '{sourceIdentifier}'. Read {count} lines.");
        _isPrepared = true;
        IsDisposed = false; // Mark as "not disposed" for the purpose of the ILogSource contract if reused
        return Task.FromResult(count);
    }

    void ILogSource.StartMonitoring(Action? callback)
    {
        _fileResetCallbackFromStartMonitoring = callback;
        Start();
    }
    void ILogSource.StopMonitoring() => Stop();

    public void Start()
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (!_isPrepared) throw new InvalidOperationException("PrepareAndGetInitialLinesAsync must be called successfully before Start.");
        if (!_isMonitoring)
        {
            StartCallCount++;
            _isMonitoring = true;
            Debug.WriteLine($"---> MockLogSource: Start called for '{PreparedSourceIdentifier}'. Now monitoring.");
        }
        else
        {
            Debug.WriteLine($"---> MockLogSource: Start called for '{PreparedSourceIdentifier}' but already monitoring.");
        }
    }

    public void Stop()
    {
        if (IsActuallyDisposed) return;
        if (_isMonitoring)
        {
            StopCallCount++;
            _isMonitoring = false;
            Debug.WriteLine($"---> MockLogSource: Stop called for '{PreparedSourceIdentifier}'. Monitoring stopped.");
        }
    }

    public void Restart()
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        RestartCallCount++;
        Debug.WriteLine($"---> MockLogSource: Restart called for '{PreparedSourceIdentifier}'.");
        Stop();
        Start();
    }

    public void UpdateRate(int newLinesPerSecond)
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        UpdateRateCallCount++;
        LastRateUpdate = newLinesPerSecond;
        LinesPerSecond = newLinesPerSecond;
        Debug.WriteLine($"---> MockLogSource: UpdateRate called ({newLinesPerSecond}) for '{PreparedSourceIdentifier}'.");
    }

    public void EmitLine(string line)
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        if (!_isMonitoring)
        {
            Debug.WriteLine($"---> MockLogSource: EmitLine called for '{line}' but not monitoring. Ignored.");
            return;
        }
        Debug.WriteLine($"---> MockLogSource: Emitting line: '{line}'");
        _logLinesSubject.OnNext(line);
    }

    public void EmitError(Exception ex)
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        Debug.WriteLine($"---> MockLogSource: Emitting error: {ex.Message}");
        _logLinesSubject.OnError(ex);
    }

    public void EmitCompletion()
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        Debug.WriteLine($"---> MockLogSource: Emitting completion.");
        _logLinesSubject.OnCompleted();
    }

    public void SimulateFileResetCallback()
    {
        if (IsActuallyDisposed) throw new ObjectDisposedException(nameof(MockLogSource));
        Debug.WriteLine($"---> MockLogSource: Simulating file reset callback invocation.");
        _fileResetCallbackFromStartMonitoring?.Invoke();
    }

    public Task GenerateBurstAsync(int lineCount)
    {
        throw new NotImplementedException("Burst generation not implemented in MockLogSource.");
    }
    public int ErrorFrequency { get; set; }

    // This Dispose is what LogDataProcessor calls.
    // For the mock, we want it to be re-preparable if the test requires it.
    public void Dispose()
    {
        if (IsActuallyDisposed) return; // Prevent multiple "actual" disposals
        Debug.WriteLine($"---> MockLogSource: Dispose called for '{PreparedSourceIdentifier}'.");
        IsDisposed = true; // Mark as disposed according to IDisposable contract
                           // but don't necessarily prevent re-use in the mock for testing.
        Stop();
        if (!_logLinesSubject.IsDisposed) // Only complete/dispose if not already done
        {
            _logLinesSubject.OnCompleted();
        }
        // Don't set IsActuallyDisposed = true here if we want it to be re-preparable for the test.
        // If we truly want to ensure it's not used again, set IsActuallyDisposed = true and dispose _logLinesSubject.
        // For the truncation test, we need it to be revivable.
        // Let's assume LogDataProcessor.Dispose() is the "final" dispose.
        // What we need is for MockLogSourceProvider to provide a "fresh" or "reset" mock.
        // The issue is when LoadLogFileCoreAsync calls Deactivate and then Activate,
        // and Deactivate causes LogDataProcessor to Dispose its current LogSource.
    }

    // Add a method to truly dispose for test cleanup if needed.
    public void HardDispose()
    {
        if (IsActuallyDisposed) return;
        IsActuallyDisposed = true;
        IsDisposed = true;
        Stop();
        _logLinesSubject.OnCompleted();
        _logLinesSubject.Dispose();
        GC.SuppressFinalize(this);
        Debug.WriteLine($"---> MockLogSource: HardDispose called for '{PreparedSourceIdentifier}'.");
    }
}
