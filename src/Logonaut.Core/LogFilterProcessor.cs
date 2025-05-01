using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Logonaut.Common;
using Logonaut.Filters;

namespace Logonaut.Core;

public class LogFilterProcessor : ILogFilterProcessor
{
    private readonly LogDocument _logDocument;
    private readonly ILogSource _logSource;
    private readonly SynchronizationContext _uiContext;
    private readonly IScheduler _backgroundScheduler;

    private readonly BehaviorSubject<FilteredUpdateBase> _filteredUpdatesSubject = new(new ReplaceFilteredUpdate(Array.Empty<FilteredLogLine>()));
    private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0);
    private readonly CompositeDisposable _disposables = new();

    private long _currentLineIndex = 0;
    private IFilter _currentFilter = new TrueFilter();
    private int _currentContextLines = 0;
    private readonly Subject<(IFilter filter, int contextLines)> _filterChangeTriggerSubject = new();

    private bool _isInitialLoadInProgress = false;
    private readonly object _stateLock = new object();

    private readonly Action<string> _addLineToDocumentCallback;

    // Configuration constants for incremental update buffering
    private const int LineBufferSize = 50; // Process lines in batches of 50
    private readonly TimeSpan _lineBufferTimeSpan = TimeSpan.FromMilliseconds(100); // Or process after 100ms
    private readonly TimeSpan _filterDebounceTime = TimeSpan.FromMilliseconds(200); // Slightly increased debounce for settings changes

    public IObservable<FilteredUpdateBase> FilteredUpdates => _filteredUpdatesSubject.AsObservable();
    public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable();

    public LogFilterProcessor(
        ILogSource logSource,
        LogDocument logDocument,
        SynchronizationContext uiContext,
        Action<string> addLineToDocumentCallback,
        IScheduler? backgroundScheduler = null)
    {
        _logSource = logSource;
        _logDocument = logDocument;
        _uiContext = uiContext;
        _backgroundScheduler = backgroundScheduler ?? TaskPoolScheduler.Default;
        _addLineToDocumentCallback = addLineToDocumentCallback;

        Debug.WriteLine($"---> LogFilterProcessor constructor {this.GetType().Name} constructed. Received LogSource: {logSource?.GetType().Name ?? "null"}");

        InitializePipelines();

        _disposables.Add(_filterChangeTriggerSubject); // Add trigger subject to disposables
        _disposables.Add(_filteredUpdatesSubject);
        _disposables.Add(_totalLinesSubject);
    }

    private void InitializePipelines()
    {
        // --- Pipeline 1: Incremental Updates for New Log Lines ---
        var incrementalUpdatePipeline = _logSource.LogLines
            .Select(line => {
                // Assign original index and add to document
                // Note: _currentLineIndex must track the *next* index to assign
                var newIndex = Interlocked.Read(ref _currentLineIndex); // Read current count *before* adding
                Interlocked.Increment(ref _currentLineIndex); // Increment for the next line
                _totalLinesSubject.OnNext(_totalLinesSubject.Value + 1); // Update total count
                _addLineToDocumentCallback(line);
                return new OriginalLineInfo((int)newIndex, line); // Pass 0-based index
            })
            .ObserveOn(_backgroundScheduler) // Shift further processing to background
            .Where(_ => { lock (_stateLock) return !_isInitialLoadInProgress; }) // Don't process new lines during initial load
            .Buffer(_lineBufferTimeSpan, LineBufferSize, _backgroundScheduler) // Buffer lines by time or count
            .Where(buffer => buffer.Any()) // Only process non-empty buffers
            .Select(bufferedLines => {
                // Apply incremental filter to the buffer
                IFilter filter;
                int context;
                lock (_stateLock) { // Access current filter settings safely
                    filter = _currentFilter;
                    context = _currentContextLines;
                }
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Applying incremental filter to batch of {bufferedLines.Count} lines.");
                // ApplyFilterToSubset needs the full document for context lookup
                var appendResult = FilterEngine.ApplyFilterToSubset(bufferedLines, _logDocument, filter, context);
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Incremental filter yielded {appendResult.Count} lines (incl. context).");
                return appendResult;
            })
            .Where(result => result.Any()) // Only emit if the incremental filter yielded results
            .Select(lines => new AppendFilteredUpdate(lines)); // Map to AppendFilteredUpdate type

        // --- Pipeline 2: Full Re-Filter for Settings Changes ---
        var fullRefilterPipeline = _filterChangeTriggerSubject
            .ObserveOn(_backgroundScheduler) // Shift processing to background
            .Throttle(_filterDebounceTime, _backgroundScheduler) // Debounce setting changes
            .Select(settingsTuple => {
                lock (_stateLock) { // Update current settings safely
                    _currentFilter = settingsTuple.filter;
                    _currentContextLines = settingsTuple.contextLines;
                }
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Applying full filter (Settings Change or Initial). Context={settingsTuple.contextLines}");
                // Apply full filter
                var fullResult = FilterEngine.ApplyFilters(_logDocument, settingsTuple.filter, settingsTuple.contextLines);
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Full filter yielded {fullResult.Count} lines.");

                bool wasInitial = false;
                lock (_stateLock) { wasInitial = _isInitialLoadInProgress; }
                if (wasInitial)
                {
                    // Set total lines *after* the initial filtering gives the final count
                    // This assumes the first UpdateFilterSettings call *after* Reset marks the end of initial lines processing.
                    long docCount = _logDocument.Count;
                     // Update the total line count state *here* after initial load calculation
                    Interlocked.Exchange(ref _currentLineIndex, docCount);
                     // Publish the count to the ViewModel
                    _totalLinesSubject.OnNext(docCount);
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Updated TotalLinesSubject and _currentLineIndex after initial filter: {docCount}");
                }
                return new ReplaceFilteredUpdate(fullResult); // Map to ReplaceFilteredUpdate type
            });

        // --- Merge Pipelines and Subscribe ---
        var mergedPipelineSubscription = Observable.Merge(
                incrementalUpdatePipeline.OfType<FilteredUpdateBase>(), // Cast to base type for merge
                fullRefilterPipeline.OfType<FilteredUpdateBase>()     // Cast to base type for merge
            )
            .ObserveOn(_uiContext) // Switch to UI thread *before* the final subscription
            .Subscribe(
                update => { // Receives ReplaceFilteredUpdate OR AppendFilteredUpdate
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Sending {update.GetType().Name} to UI. Lines={update.Lines.Count}. _isInitialLoadInProgress(BeforeSend) = {_isInitialLoadInProgress}");
                    bool wasInitialLoad = false;
                    lock (_stateLock) { wasInitialLoad = _isInitialLoadInProgress; }

                    _filteredUpdatesSubject.OnNext(update); // Emit the update

                    // If this update was the result of the initial load (always a Replace), mark initial load as finished
                    if (wasInitialLoad && update is ReplaceFilteredUpdate)
                    {
                        lock (_stateLock) { _isInitialLoadInProgress = false; }
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Initial Load Filter Complete. _isInitialLoadInProgress=false");
                    }
                },
                ex => {
                    lock (_stateLock) { _isInitialLoadInProgress = false; } // Ensure flag is reset on error
                    HandlePipelineError("Log Processing Pipeline Error", ex);
                }
            );

        _disposables.Add(mergedPipelineSubscription);
    }

    // No changes needed for ApplyFullFilter helper
    // private IReadOnlyList<FilteredLogLine> ApplyFullFilter(IFilter filter, int contextLines) { ... }

    public void UpdateFilterSettings(IFilter newFilter, int contextLines)
    {
        var filterToUse = newFilter ?? new TrueFilter();
        var contextToUse = Math.Max(0, contextLines);

        // Trigger the pipeline responsible for settings changes
        _filterChangeTriggerSubject.OnNext((filterToUse, contextToUse));
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: UpdateFilterSettings called. Triggering full re-filter.");
    }

    public void Reset()
    {
        lock (_stateLock)
        {
            _isInitialLoadInProgress = true;
        }
        Interlocked.Exchange(ref _currentLineIndex, 0); // Reset internal indexer
        // LogDocument clearing is handled by MainViewModel via _logSource.Prepare...
        _totalLinesSubject.OnNext(0); // Reset displayed count immediately
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Reset() called. _isInitialLoadInProgress=true");
    }

    private void HandlePipelineError(string contextMessage, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{contextMessage}: {ex}");
        // Ensure error is propagated to UI thread safely
        _uiContext.Post(_ => _filteredUpdatesSubject.OnError(ex), null);
    }

    public void Dispose()
    {
        // Dispose filterChangeTriggerSubject first if needed
        if (!_filterChangeTriggerSubject.IsDisposed)
        {
            _filterChangeTriggerSubject.OnCompleted();
            _filterChangeTriggerSubject.Dispose();
        }

        // Complete output subjects BEFORE disposing subscriptions
        if (!_filteredUpdatesSubject.IsDisposed)
        {
            _filteredUpdatesSubject.OnCompleted();
            _filteredUpdatesSubject.Dispose();
        }

        if (!_totalLinesSubject.IsDisposed)
        {
            _totalLinesSubject.OnCompleted();
            _totalLinesSubject.Dispose();
        }

        // Disposes merged pipeline subscription
        _disposables.Dispose();

        // Processor should NOT dispose the _logSource it received.
    }
}
