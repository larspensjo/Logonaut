// Logonaut.UI.Tests/Mocks/MockServices.cs (or individual files)
using System;
using System.Collections.Generic;
using System.Reactive; // For Unit
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Diagnostics; // Added for Debug
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

    // --- File Dialog Service Mock ---
    public class MockFileDialogService : IFileDialogService
    {
        public string? FileToReturn { get; set; } = "C:\\fake\\log.txt";
        public bool ShouldCancel { get; set; } = false;
        public string? OpenFile(string title, string filter, string? initialDirectory = null) => ShouldCancel ? null : FileToReturn;
    }

    // --- Log Filter Processor Mock ---
    public class MockReactiveFilteredLogStream : IReactiveFilteredLogStream
    {
        private readonly Subject<FilteredUpdateBase> _filteredUpdatesSubject = new Subject<FilteredUpdateBase>();
        private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0); // <<< ADDED
        private bool _isDisposed = false;

        // --- ILogFilterProcessor Implementation ---
        public IObservable<FilteredUpdateBase> FilteredUpdates => _filteredUpdatesSubject.AsObservable();
        public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable(); // <<< ADDED

        // --- Mock Control Properties & Methods ---
        public int ResetCallCount { get; private set; } = 0;
        public int UpdateFilterSettingsCallCount { get; private set; } = 0;
        public (IFilter? Filter, int ContextLines)? LastFilterSettings { get; private set; }
        public long CurrentSimulatedTotalLines => _totalLinesSubject.Value; // Helper to check current value

        public void Reset()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(MockReactiveFilteredLogStream));
            ResetCallCount++;
            _totalLinesSubject.OnNext(0); // Reset total lines count
            // Optionally simulate the empty Replace update if tests rely on it
            // SimulateFilteredUpdate(new FilteredUpdate(Array.Empty<FilteredLogLine>()));
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
            if (_isDisposed) throw new ObjectDisposedException(nameof(MockReactiveFilteredLogStream));
            UpdateFilterSettingsCallCount++;
            LastFilterSettings = (newFilter, contextLines);
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> MockLogFilterProcessor: UpdateFilterSettings called. Triggering full re-filter.");
        }

        /// <summary>
        /// Simulates the processor emitting a ReplaceFilteredUpdate.
        /// Use this for tests simulating initial loads or filter setting changes.
        /// </summary>
        /// <param name="lines">The complete list of lines for the replacement.</param>
        public void SimulateReplaceUpdate(List<FilteredLogLine> lines)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(MockReactiveFilteredLogStream));
            var update = new ReplaceFilteredUpdate(lines, false); // Create specific type
            _filteredUpdatesSubject.OnNext(update);        // Emit
        }

        /// <summary>
        /// Simulates the processor emitting an AppendFilteredUpdate.
        /// Use this for tests simulating incremental updates from new log lines.
        /// </summary>
        /// <param name="linesToAppend">The list of new/context lines to append.</param>
        public void SimulateAppendUpdate(List<FilteredLogLine> linesToAppend)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(MockReactiveFilteredLogStream));
            var update = new AppendFilteredUpdate(linesToAppend); // Create specific type
            _filteredUpdatesSubject.OnNext(update);           // Emit
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