using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive; // For Unit
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Logonaut.Common;
using Logonaut.Filters;

namespace Logonaut.Core
{
    /// <summary>
    /// Processes log lines against filters reactively, handling incremental updates and full re-filters.
    /// </summary>
    public class LogFilterProcessor : ILogFilterProcessor
    {
        private readonly LogDocument _logDocument;
        private readonly ILogTailerService _logTailerService;
        private readonly SynchronizationContext _uiContext;

        private readonly BehaviorSubject<FilteredUpdate> _filteredUpdatesSubject = new(new FilteredUpdate(UpdateType.Replace, Array.Empty<FilteredLogLine>()));
        private readonly Subject<Unit> _resetSignal = new Subject<Unit>(); // <<< NEW: Signal for reset completion
        private readonly CompositeDisposable _disposables = new();
        private readonly IScheduler _backgroundScheduler;
        private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0);
        private long _currentLineIndex = 0; // Tracks original line numbers internally
        private IFilter _currentFilter = new TrueFilter();
        private int _currentContextLines = 0;
        private Subject<(IFilter filter, int contextLines)>? _filterChangeTriggerSubject;

        // Configuration Constants (could be made configurable)
        private const int LineBufferSize = 50;
        private readonly TimeSpan _lineBufferTimeSpan = TimeSpan.FromMilliseconds(100);
        private readonly TimeSpan _filterDebounceTime = TimeSpan.FromMilliseconds(300);

        public IObservable<FilteredUpdate> FilteredUpdates => _filteredUpdatesSubject.AsObservable();

        private IDisposable? _logSubscription; // Keep track of the subscription to dispose it

        public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable();

        public LogFilterProcessor(
            ILogTailerService logTailerService,
            LogDocument logDocument,
            SynchronizationContext uiContext,
            IScheduler? backgroundScheduler = null)
        {
            _logTailerService = logTailerService;
            _logDocument = logDocument;
            _uiContext = uiContext;
            _backgroundScheduler = backgroundScheduler ?? TaskPoolScheduler.Default;

            InitializePipelines();

            _disposables.Add(_filteredUpdatesSubject);
            _disposables.Add(_resetSignal);
            _disposables.Add(_totalLinesSubject);
            _disposables.Add(Disposable.Create(() => _logSubscription?.Dispose()));
        }

        private void InitializePipelines()
        {
            _logSubscription?.Dispose(); // Clean up previous if any

            _logSubscription = _logTailerService.LogLines
                .Select(line =>
                {
                    var newIndex = Interlocked.Increment(ref _currentLineIndex); // Get the NEW index
                    var lineWithIndex = new { LineText = line, OriginalIndex = newIndex };
                    _logDocument.AppendLine(line);
                    _totalLinesSubject.OnNext(newIndex);
                    return lineWithIndex;
                })
                .Buffer(_lineBufferTimeSpan, LineBufferSize, _backgroundScheduler)
                .Where(buffer => buffer.Count > 0)
                // Apply the *currently cached* filter incrementally
                .Select(buffer => ApplyIncrementalFilter(buffer.Select(item => (item.LineText, item.OriginalIndex)).ToList(), _currentFilter)) // Pass filter explicitly
                .Where(filteredLines => filteredLines.Count > 0)
                .ObserveOn(_uiContext)
                .Subscribe(
                    matchedLines => _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Append, matchedLines)),
                    ex => HandlePipelineError("Incremental Filtering Error", ex)
                );

            // Trigger 1: Reset completes (Initial Load)
            // When Reset() is called, it signals _resetSignal AFTER clearing state.
            // We wait for this signal AND the initial file read completion.
            var initialLoadTrigger = _resetSignal
                .Select(_ => _logTailerService.InitialReadComplete // Switch to the completion signal from the tailer
                            .Timeout(TimeSpan.FromSeconds(60), _backgroundScheduler) // Add timeout for safety
                            .Catch<Unit, Exception>(ex => // Handle timeout or other errors
                            {
                                Debug.WriteLine($"!!! Initial read signal error/timeout: {ex.Message}");
                                // Propagate error or return empty signal? Let's propagate.
                                return Observable.Throw<Unit>(new Exception("Initial log read failed or timed out.", ex));
                            }))
                .Switch() // Switch to the inner observable (InitialReadComplete)
                .Take(1); // Only take the first completion signal after reset

            // Trigger 2: Filter Settings Changed (Subsequent Loads)
            // Reintroduce a way to trigger updates manually via UpdateFilterSettings
            var filterChangeTrigger = new Subject<(IFilter filter, int contextLines)>();
            _disposables.Add(filterChangeTrigger); // Dispose this subject too

            // Combine triggers: Merge initial load signal (as Unit) and manual filter changes
            var combinedTrigger = Observable.Merge(
                    initialLoadTrigger.Select(_ => (Filter: _currentFilter, Context: _currentContextLines)), // Use current settings on initial load
                    filterChangeTrigger // Use settings passed via UpdateFilterSettings
                );


            var fullRefilterSubscription = combinedTrigger
                .ObserveOn(_backgroundScheduler) // Ensure ApplyFullFilter runs on background
                // Use the tuple directly here. Item1 is the filter, Item2 is contextLines.
                .Select(settingsTuple => // Perform filtering on background
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Starting ApplyFullFilter triggered by {(settingsTuple.Item1 == _currentFilter ? "Initial Load" : "Setting Change")}.");
                    var result = ApplyFullFilter(settingsTuple.Item1, settingsTuple.Item2);
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Finished ApplyFullFilter. Got {result.Count} lines.");
                    return result;
                })
                .ObserveOn(_uiContext) // Marshal result to UI thread
                .Subscribe(
                    newFilteredLines => _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Replace, newFilteredLines)),
                    ex => HandlePipelineError("Full Re-Filtering Error", ex)
                );

            _disposables.Add(fullRefilterSubscription);

            // Need a local field to store the filterChangeTrigger subject
            _filterChangeTriggerSubject = filterChangeTrigger;
        }

        private List<FilteredLogLine> ApplyIncrementalFilter(IList<(string LineText, long OriginalIndex)> buffer, IFilter filter)
        {
            // Logic remains the same, but uses the passed filter
            return buffer
                .Select(item => new { Item = item, IsMatch = filter.IsMatch(item.LineText) })
                .Where(result => result.IsMatch)
                .Select(result => new FilteredLogLine((int)result.Item.OriginalIndex, result.Item.LineText))
                .ToList();
        }

        private IReadOnlyList<FilteredLogLine> ApplyFullFilter(IFilter filter, int contextLines)
        {
            // Apply filter to the entire document snapshot
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LogFitlerProcessor.ApplyFullFilter() before ApplyFilters");
            var tmp = FilterEngine.ApplyFilters(_logDocument, filter, contextLines);
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LogFitlerProcessor.ApplyFullFilter() after ApplyFilters");
            return tmp;
        }

        public void UpdateFilterSettings(IFilter newFilter, int contextLines)
        {
            _currentFilter = newFilter ?? new TrueFilter();
            _currentContextLines = Math.Max(0, contextLines);

            // Trigger the manual filter change subject
            _filterChangeTriggerSubject?.OnNext((_currentFilter, _currentContextLines));
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: UpdateFilterSettings called. Triggering re-filter.");
        }

        public void Reset()
        {
            // Clear document, reset counter FIRST
            Interlocked.Exchange(ref _currentLineIndex, 0);
            _logDocument.Clear();
            _currentFilter = new TrueFilter();
            _currentContextLines = 0;
            _totalLinesSubject.OnNext(0);

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Reset() called.");

            // Signal that Reset has completed its synchronous part
            _resetSignal.OnNext(Unit.Default);
        }


        private void HandlePipelineError(string contextMessage, Exception ex)
        {
            // Instead of throwing, push the error to the output subject
            System.Diagnostics.Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}{contextMessage}: {ex}"); // Keep logging
            _filteredUpdatesSubject.OnError(ex); // Propagate error to subscribers
        }

        private void UpdateFilterSettings()
        {
            // Consider logging or exposing errors via another observable
            // For now, just output to debug. A robust app would need more.
        }

        private bool _isDisposed = false;
        public void Dispose()
        {
            if (_isDisposed) return;
                _isDisposed = true;
                _totalLinesSubject?.OnCompleted();
                _filteredUpdatesSubject?.OnCompleted();
                _resetSignal?.OnCompleted(); // Complete signals
                _filterChangeTriggerSubject?.OnCompleted();

                _logSubscription?.Dispose();
                _disposables.Dispose(); // Disposes subjects and remaining subscriptions

                // Subjects are added to _disposables, no need to dispose again
        }
    }
}