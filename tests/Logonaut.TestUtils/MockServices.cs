// Logonaut.UI.Tests/Mocks/MockServices.cs (or individual files)
using System;
using System.Collections.Generic;
using System.Reactive; // For Unit
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Services;

namespace Logonaut.TestUtils
{

    // We don't want for WPF asynchronous operations to run in the background, so we create a custom SynchronizationContext
    // that runs immediately. This is used in the tests to ensure that any UI updates are processed immediately.
    public class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            d(state); // Run immediately, synchronously
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state); // Also immediate, for Send
        }
    }

    // --- Settings Service Mock ---
    public class MockSettingsService : ISettingsService
    {
        public LogonautSettings SettingsToReturn { get; set; } = CreateDefaultTestSettings();
        public LogonautSettings? SavedSettings { get; private set; }

        public LogonautSettings LoadSettings() => SettingsToReturn;

        public void SaveSettings(LogonautSettings settings) => SavedSettings = settings;

        public static LogonautSettings CreateDefaultTestSettings() => new LogonautSettings
        {
            FilterProfiles = new List<FilterProfile> { new FilterProfile("Default", null) },
            LastActiveProfileName = "Default",
            ContextLines = 0,
            ShowLineNumbers = true,
            HighlightTimestamps = true,
            IsCaseSensitiveSearch = false
        };

        public void ResetSettings()
        {
            SavedSettings = null;
        }
    }

    public class MockLogTailerService : ILogTailerService
    {
        private readonly Subject<string> _logLinesSubject = new Subject<string>();
        // Use AsyncSubject for the completion signal: it only emits the last value (or error)
        // right before completing, which perfectly models a one-time completion event.
        private AsyncSubject<Unit> _initialReadCompleteSubject = new AsyncSubject<Unit>(); // Initialize

        public string? ChangedFilePath { get; private set; }
        public bool IsDisposed { get; private set; } = false;
        public bool IsStopped { get; private set; } = false;

        // --- ILogTailerService Implementation ---

        public IObservable<string> LogLines => _logLinesSubject.AsObservable();

        // NEW: Implement the InitialReadComplete property
        public IObservable<Unit> InitialReadComplete => _initialReadCompleteSubject.AsObservable();

        // NEW: Implement the async ChangeFileAsync method
        public Task ChangeFileAsync(string filePath)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogTailerService));
            if (filePath == "C:\\throw\\error.log") throw new System.IO.FileNotFoundException("Mock file not found");

            ChangedFilePath = filePath;
            IsStopped = false;

            // CRUCIAL: Reset the completion signal for the new file "load".
            // Dispose the old one and create a new one.
            if (!_initialReadCompleteSubject.IsDisposed) // Check if not already disposed
            {
                if (!_initialReadCompleteSubject.IsCompleted) // Signal error if not completed? Optional.
                {
                    _initialReadCompleteSubject.OnError(new OperationCanceledException("File changed before initial read completed."));
                }
                _initialReadCompleteSubject.Dispose();
            }
            _initialReadCompleteSubject = new AsyncSubject<Unit>();

            // Since mock setup is synchronous, return a completed task.
            return Task.CompletedTask;
        }

        // Keep the old synchronous ChangeFile method ONLY if absolutely needed for old tests.
        // Ideally, update tests and remove this. If kept, ensure it also resets the signal.
        [Obsolete("Use ChangeFileAsync instead.")]
        public void ChangeFile(string filePath)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogTailerService));
            if (filePath == "C:\\throw\\error.log") throw new System.IO.FileNotFoundException("Mock file not found");

            ChangedFilePath = filePath;
            IsStopped = false;

             // Reset completion signal here too if keeping sync method
            if (!_initialReadCompleteSubject.IsDisposed)
            {
                if (!_initialReadCompleteSubject.IsCompleted)
                {
                    _initialReadCompleteSubject.OnError(new OperationCanceledException("File changed before initial read completed."));
                }
                _initialReadCompleteSubject.Dispose();
            }
            _initialReadCompleteSubject = new AsyncSubject<Unit>();
        }

        public void StopTailing()
        {
            if (IsDisposed) return;
            IsStopped = true;
            // Optional: Signal cancellation if the initial read wasn't completed yet.
            if (!_initialReadCompleteSubject.IsCompleted && !_initialReadCompleteSubject.IsDisposed)
            {
                _initialReadCompleteSubject.OnError(new OperationCanceledException("Tailing stopped during initial read."));
            }
        }

        // --- Simulation Methods for Tests ---

        public void EmitLine(string line)
        {
            if (!IsDisposed && !IsStopped) _logLinesSubject.OnNext(line);
        }

        public void EmitError(Exception ex)
        {
            if (!IsDisposed && !IsStopped) _logLinesSubject.OnError(ex);
        }

        // NEW: Method for tests to simulate the end of the initial read
        public void SimulateInitialReadComplete()
        {
            if (!IsDisposed && !_initialReadCompleteSubject.IsCompleted)
            {
                _initialReadCompleteSubject.OnNext(Unit.Default); // Required before OnCompleted for AsyncSubject
                _initialReadCompleteSubject.OnCompleted();
            }
        }

        // NEW: Method for tests to simulate an error during initial read
        public void SimulateInitialReadError(Exception ex)
        {
            if (!IsDisposed && !_initialReadCompleteSubject.IsCompleted)
            {
                _initialReadCompleteSubject.OnError(ex);
            }
        }

        // --- IDisposable ---

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;

            // Complete/Dispose log lines subject
            _logLinesSubject.OnCompleted();
            _logLinesSubject.Dispose();

            // Complete/Dispose initial read subject - signal error if not done
            if (!_initialReadCompleteSubject.IsCompleted && !_initialReadCompleteSubject.IsDisposed)
            {
                _initialReadCompleteSubject.OnError(new ObjectDisposedException(nameof(MockLogTailerService)));
            }
            if (!_initialReadCompleteSubject.IsDisposed)
            {
            _initialReadCompleteSubject.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }

    // --- File Dialog Service Mock ---
    public class MockFileDialogService : IFileDialogService
    {
        public string? FileToReturn { get; set; } = "C:\\fake\\log.txt";
        public bool ShouldCancel { get; set; } = false;
        public string? OpenFile(string title, string filter) => ShouldCancel ? null : FileToReturn;
    }

    // --- Log Filter Processor Mock ---
    public class MockLogFilterProcessor : ILogFilterProcessor
    {
        private readonly Subject<FilteredUpdate> _filteredUpdatesSubject = new Subject<FilteredUpdate>();
        private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0); // <<< ADDED
        private bool _isDisposed = false;

        // --- ILogFilterProcessor Implementation ---
        public IObservable<FilteredUpdate> FilteredUpdates => _filteredUpdatesSubject.AsObservable();
        public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable(); // <<< ADDED

        // --- Mock Control Properties & Methods ---
        public int ResetCallCount { get; private set; } = 0;
        public int UpdateFilterSettingsCallCount { get; private set; } = 0;
        public (IFilter? Filter, int ContextLines)? LastFilterSettings { get; private set; }
        public long CurrentSimulatedTotalLines => _totalLinesSubject.Value; // Helper to check current value

        public void Reset()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(MockLogFilterProcessor));
            ResetCallCount++;
            _totalLinesSubject.OnNext(0); // Reset total lines count
            // Optionally simulate the empty Replace update if tests rely on it
            // SimulateFilteredUpdate(new FilteredUpdate(UpdateType.Replace, Array.Empty<FilteredLogLine>()));
        }

        public void ResetCounters()
        {
            ResetCallCount = 0;
            UpdateFilterSettingsCallCount = 0;
            LastFilterSettings = null;
            // Don't reset total lines subject here, Reset() handles that
        }

        public void UpdateFilterSettings(IFilter newFilter, int contextLines)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(MockLogFilterProcessor));
            UpdateFilterSettingsCallCount++;
            LastFilterSettings = (newFilter, contextLines);
        }

        // --- Simulation Methods for Tests ---
        public void SimulateFilteredUpdate(FilteredUpdate update)
        {
            if (!_isDisposed) _filteredUpdatesSubject.OnNext(update);
        }

        public void SimulateTotalLinesUpdate(long newTotal) // <<< ADDED
        {
            if (!_isDisposed) _totalLinesSubject.OnNext(newTotal);
        }

        public void SimulateError(Exception ex)
        {
            if (!_isDisposed)
            {
                // Simulate error on both streams for comprehensive testing
                _filteredUpdatesSubject.OnError(ex);
                _totalLinesSubject.OnError(ex);
            }
        }

        public void SimulateCompletion()
        {
            if (!_isDisposed)
            {
                // Simulate completion on both streams
                _filteredUpdatesSubject.OnCompleted();
                _totalLinesSubject.OnCompleted();
            }
        }

        // --- IDisposable Implementation ---
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _filteredUpdatesSubject.OnCompleted();
            _filteredUpdatesSubject.Dispose();

            _totalLinesSubject.OnCompleted(); // <<< ADDED
            _totalLinesSubject.Dispose();    // <<< ADDED

            GC.SuppressFinalize(this);
        }
    }
}