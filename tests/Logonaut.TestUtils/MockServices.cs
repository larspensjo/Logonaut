// Logonaut.UI.Tests/Mocks/MockServices.cs (or individual files)
using System;
using System.Collections.Generic;
using System.Reactive; // For Unit
using System.Reactive.Subjects;
using System.Reactive.Linq;
using System.Diagnostics; // Added for Debug
using Newtonsoft.Json; // Required for JsonConvert
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
        // Settings returned by LoadSettings
        public LogonautSettings SettingsToReturn { get; set; } = CreateDefaultTestSettings();

        // Settings captured by the last call to SaveSettings (now a deep clone)
        public LogonautSettings? SavedSettings { get; private set; }

        // Counters
        public int LoadCalledCount { get; private set; } = 0;
        public int SaveCalledCount { get; private set; } = 0;

        /// <summary>
        /// Loads settings, returning a deep clone of SettingsToReturn.
        /// </summary>
        public LogonautSettings LoadSettings()
        {
            LoadCalledCount++;
            Debug.WriteLine($"---> MockSettingsService: LoadSettings called. Count: {LoadCalledCount}");
            try
            {
                // Return a DEEP CLONE to prevent tests modifying the source object
                string json = JsonConvert.SerializeObject(SettingsToReturn, Formatting.None,
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });
                return JsonConvert.DeserializeObject<LogonautSettings>(json,
                    new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All })
                    ?? CreateDefaultTestSettings(); // Fallback if deserialization fails
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error during deep clone in LoadSettings", ex);
            }
        }

        /// <summary>
        /// Saves settings by storing a deep clone of the provided settings object.
        /// </summary>
        public void SaveSettings(LogonautSettings settings)
        {
           // --- Check for duplicate FilterProfiles by name ---
            var duplicateProfile = settings.FilterProfiles
            .GroupBy(profile => profile.Name)
            .FirstOrDefault(group => group.Count() > 1);

            if (duplicateProfile != null)
                throw new InvalidOperationException($"Duplicate FilterProfile name detected: {duplicateProfile.Key}");

            SaveCalledCount++;
            Debug.WriteLine($"---> MockSettingsService: SaveSettings called. Count: {SaveCalledCount}");

            // --- Perform a deep clone using JSON serialization ---
            try
            {
                // Serialize the incoming settings object
                string json = JsonConvert.SerializeObject(settings, Formatting.None,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All, // Crucial for IFilter polymorphism
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore // Usually safe
                    });

                // Deserialize back into a new object and store it
                this.SavedSettings = JsonConvert.DeserializeObject<LogonautSettings>(json,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All
                    });
                Debug.WriteLine($"---> MockSettingsService: Deep clone successful for SaveSettings.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error during deep clone in SaveSettings", ex);
            }
            // --- End deep clone ---
        }

        /// <summary>
        /// Creates default settings used for initialization or fallbacks.
        /// </summary>
        public static LogonautSettings CreateDefaultTestSettings() => new LogonautSettings
        {
            FilterProfiles = new List<FilterProfile> { new FilterProfile("Default", null) },
            LastActiveProfileName = "Default",
            ContextLines = 0,
            ShowLineNumbers = true,
            HighlightTimestamps = true,
            IsCaseSensitiveSearch = false,
            // Add simulator defaults if needed for tests
            SimulatorLPS = 10.0,
            SimulatorErrorFrequency = 100.0,
            SimulatorBurstSize = 1000.0
        };

        /// <summary>
        /// Resets the state of the mock service (call counts, saved settings).
        /// </summary>
        public void Reset() // Renamed from ResetSettings for clarity
        {
            SavedSettings = null;
            SaveCalledCount = 0;
            LoadCalledCount = 0;
            // Don't reset SettingsToReturn here, let tests configure it via its public setter if needed
            Debug.WriteLine("---> MockSettingsService: Reset.");
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