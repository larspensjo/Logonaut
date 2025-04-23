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
    private readonly LogDocument _logDocument; // Reference provided by ViewModel
    private readonly ILogTailerService _logTailerService;
    private readonly SynchronizationContext _uiContext;
    private readonly IScheduler _backgroundScheduler;

    private readonly BehaviorSubject<FilteredUpdate> _filteredUpdatesSubject = new(new FilteredUpdate(UpdateType.Replace, Array.Empty<FilteredLogLine>()));
    private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0);
    private readonly CompositeDisposable _disposables = new();

    private long _currentLineIndex = 0; // Tracks original numbers for *new* lines post-initial load
    private IFilter _currentFilter = new TrueFilter();
    private int _currentContextLines = 0;
    private Subject<(IFilter filter, int contextLines)>? _filterChangeTriggerSubject;
    private IDisposable? _logSubscription;

    private bool _isInitialLoadInProgress = false; // Flag remains useful
    private readonly object _stateLock = new object();

    private readonly Action<string> _addLineToDocumentCallback;

    // Config Constants
    private const int LineBufferSize = 50;
    private readonly TimeSpan _lineBufferTimeSpan = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _filterDebounceTime = TimeSpan.FromMilliseconds(300); // Debounce for manual changes

    public IObservable<FilteredUpdate> FilteredUpdates => _filteredUpdatesSubject.AsObservable();
    public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable();

    public LogFilterProcessor(
        ILogTailerService logTailerService,
        LogDocument logDocument,
        SynchronizationContext uiContext,
        Action<string> addLineToDocumentCallback,
        IScheduler? backgroundScheduler = null)
    {
        _logTailerService = logTailerService;
        _logDocument = logDocument;
        _uiContext = uiContext;
        _backgroundScheduler = backgroundScheduler ?? TaskPoolScheduler.Default;
        _addLineToDocumentCallback = addLineToDocumentCallback;

        InitializePipelines();

        _disposables.Add(_filteredUpdatesSubject);
        _disposables.Add(_totalLinesSubject);
    }

    private void InitializePipelines()
    {
        _logSubscription?.Dispose(); // Clean up previous if any

        // Pipeline for new lines from the tailer. Responsibility:
        // 1. Receive a new line.
        // 2. Tell MainViewModel (via callback) to add it to the canonical LogDoc.
        // 3. Trigger the other pipeline (the full refilter pipeline) to re-evaluate everything.
        _logSubscription = _logTailerService.LogLines
            .Select(line => {
                // 1. Assign index (still useful for total count)
                var newIndex = Interlocked.Increment(ref _currentLineIndex);
                _totalLinesSubject.OnNext(_totalLinesSubject.Value + 1);

                // 2. Invoke the CALLBACK to add the line to the MainViewModel's LogDoc
                _addLineToDocumentCallback?.Invoke(line); // <<< USE CALLBACK

                // Return value doesn't matter much, maybe just the line for debugging
                return line; // Or Unit.Default
            })
            .ObserveOn(_backgroundScheduler) // Ensure trigger happens on background
            .Where(_ => { lock (_stateLock) return !_isInitialLoadInProgress; }) // Prevent triggers during initial load
            .Subscribe(
                lineText => { // Parameter is now just the line text (or Unit)
                    // 3. Trigger the full refilter pipeline
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: New line '{lineText.Substring(0, Math.Min(lineText.Length, 20))}...' detected. Triggering full refilter check via callback.");
                    _filterChangeTriggerSubject?.OnNext((_currentFilter, _currentContextLines));
                },
                ex => HandlePipelineError("New Line Processing Error", ex)
            );
        _disposables.Add(_logSubscription); // Add to disposables

        var filterChangeTrigger = new Subject<(IFilter filter, int contextLines)>();
        _disposables.Add(filterChangeTrigger);
        _filterChangeTriggerSubject = filterChangeTrigger;

        /* Responsibilities of this pipeline:
            1. Triggered by explicit filter changes or new log lines (via _filterChangeTriggerSubject).
            2. Executes filtering logic on a background thread to keep the UI responsive.
            3. Throttles rapid triggers to avoid excessive recalculations, processing only the last request after a pause.
            4. Applies the latest filter/context settings to the entire, up-to-date LogDocument.
            5. Marshals the complete list of FilteredLogLine results safely back to the UI thread.
            6. Delivers results as a Replace update via _filteredUpdatesSubject for the ViewModel to consume.
        */
        var fullRefilterSubscription = filterChangeTrigger
            .ObserveOn(_backgroundScheduler)
            .Throttle(_filterDebounceTime, _backgroundScheduler)
            .Select(settingsTuple => {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Starting ApplyFullFilter triggered by Setting Change OR New Line.");
                    // ApplyFullFilter reads the LogDocument updated via callback
                    var result = ApplyFullFilter(settingsTuple.Item1, settingsTuple.Item2);
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Finished ApplyFullFilter. Got {result.Count} lines.");

                    // If this was the FIRST filter run (after Reset), update TotalLines count
                    bool wasInitial = false;
                    long docCount = 0; // Capture count thread-safely if needed
                    lock(_stateLock) { wasInitial = _isInitialLoadInProgress; }
                    if (wasInitial)
                    {
                        docCount = _logDocument.Count; // Read count *after* ApplyFullFilter reads it
                        _totalLinesSubject.OnNext(docCount);
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Updated TotalLinesSubject after initial filter: {docCount}");
                    }

                    return result;
                })
            .ObserveOn(_uiContext)
            .Subscribe(
                newFilteredLines => {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Sending Replace Update to UI. Lines={newFilteredLines.Count}. _isInitialLoadInProgress(BeforeSend) = { _isInitialLoadInProgress}");
                    bool wasInitialLoad = false;
                    lock(_stateLock) { wasInitialLoad = _isInitialLoadInProgress; }

                    _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Replace, newFilteredLines));

                    if (wasInitialLoad)
                    {
                        lock(_stateLock) { _isInitialLoadInProgress = false; } // Clear flag *after* sending
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Initial Load Filter Complete. _isInitialLoadInProgress=false");
                    }
                },
                ex => {
                    lock(_stateLock) { _isInitialLoadInProgress = false; } // Clear flag on error too
                    HandlePipelineError("Full Re-Filtering Error", ex);
                }
            );

        _disposables.Add(fullRefilterSubscription);
    }

    private IReadOnlyList<FilteredLogLine> ApplyFullFilter(IFilter filter, int contextLines)
    {
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LogFitlerProcessor.ApplyFullFilter() before ApplyFilters");
        var tmp = FilterEngine.ApplyFilters(_logDocument, filter, contextLines);
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LogFitlerProcessor.ApplyFullFilter() after ApplyFilters");
        return tmp;
    }

    // Called by ViewModel after Reset() and ChangeFileAsync() complete
    public void UpdateFilterSettings(IFilter newFilter, int contextLines)
    {
        _currentFilter = newFilter ?? new TrueFilter();
        _currentContextLines = Math.Max(0, contextLines);

        // Trigger the pipeline
        _filterChangeTriggerSubject?.OnNext((_currentFilter, _currentContextLines));
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: UpdateFilterSettings called. Triggering re-filter.");
    }

    public void Reset()
    {
        lock (_stateLock)
        {
            _isInitialLoadInProgress = true; // Set flag for the *next* UpdateFilterSettings call
        }
        Interlocked.Exchange(ref _currentLineIndex, 0); // Reset index for *new* lines
        // DO NOT CLEAR _logDocument here - ViewModel manages it
        _totalLinesSubject.OnNext(0); // Reset displayed count
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Reset() called. _isInitialLoadInProgress=true");
    }

    private void HandlePipelineError(string contextMessage, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{contextMessage}: {ex}");
        _filteredUpdatesSubject.OnError(ex);
    }

    public void Dispose()
    {
        // Dispose _filterChangeTriggerSubject first if needed
        _filterChangeTriggerSubject?.OnCompleted();
        _filterChangeTriggerSubject?.Dispose();
        _filterChangeTriggerSubject = null; // Prevent reuse after disposal

        // Explicitly complete the output subjects BEFORE disposing subscriptions
        // Check if not null and not already disposed (good practice)
        if (_filteredUpdatesSubject != null && !_filteredUpdatesSubject.IsDisposed)
        {
            _filteredUpdatesSubject.OnCompleted();
            _filteredUpdatesSubject.Dispose(); // Dispose the subject itself
        }

        if (_totalLinesSubject != null && !_totalLinesSubject.IsDisposed)
        {
            _totalLinesSubject.OnCompleted();
            _totalLinesSubject.Dispose(); // Dispose the subject itself
        }


        // Now dispose the container which holds the subscriptions
        _disposables.Dispose();

        // No need to dispose _logSubscription separately, _disposables handles it.
    }
}
