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

    // Config Constants
    private const int LineBufferSize = 50;
    private readonly TimeSpan _lineBufferTimeSpan = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _filterDebounceTime = TimeSpan.FromMilliseconds(300); // Debounce for manual changes

    public IObservable<FilteredUpdate> FilteredUpdates => _filteredUpdatesSubject.AsObservable();
    public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable();

    public LogFilterProcessor(
        ILogTailerService logTailerService,
        LogDocument logDocument, // Expect LogDocument from caller
        SynchronizationContext uiContext,
        IScheduler? backgroundScheduler = null)
    {
        _logTailerService = logTailerService;
        _logDocument = logDocument; // Store reference
        _uiContext = uiContext;
        _backgroundScheduler = backgroundScheduler ?? TaskPoolScheduler.Default;

        InitializePipelines();

        _disposables.Add(_filteredUpdatesSubject);
        _disposables.Add(_totalLinesSubject);
    }

    private void InitializePipelines()
    {
        _logSubscription?.Dispose(); // Clean up previous if any

            // Pipeline for NEW lines from the tailer
        _logSubscription = _logTailerService.LogLines
            .Select(line => {
                var newIndex = Interlocked.Increment(ref _currentLineIndex);
                // Assign original index relative to start *after* initial load
                // The line itself is NOT added to _logDocument here
                _totalLinesSubject.OnNext(_totalLinesSubject.Value + 1); // Increment total count seen
                return new { LineText = line, OriginalIndex = newIndex };
            })
            .Buffer(_lineBufferTimeSpan, LineBufferSize, _backgroundScheduler)
            .Where(buffer => buffer.Count > 0)
            .Select(buffer => ApplyIncrementalFilter(buffer.Select(item => (item.LineText, item.OriginalIndex)).ToList(), _currentFilter))
            .Where(filteredLines => filteredLines.Count > 0)
            .Where(_ => { lock (_stateLock) return !_isInitialLoadInProgress; }) // Prevent Append during initial filter run
            .ObserveOn(_uiContext)
            .Subscribe(
                matchedLines => {
                    // This will now only receive Append updates *after* initial load completes
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Sending Append Update to UI. Lines={matchedLines.Count}");
                    _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Append, matchedLines));
                },
                ex => HandlePipelineError("Incremental Filtering Error", ex)
            );
        _disposables.Add(_logSubscription); // Add to disposables

        // Pipeline ONLY for explicit filter setting changes
        var filterChangeTrigger = new Subject<(IFilter filter, int contextLines)>();
        _disposables.Add(filterChangeTrigger);
        _filterChangeTriggerSubject = filterChangeTrigger;

        var fullRefilterSubscription = filterChangeTrigger
            .ObserveOn(_backgroundScheduler)
                // Add debounce for manual changes
            .Throttle(_filterDebounceTime, _backgroundScheduler)
            .Select(settingsTuple => {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Starting ApplyFullFilter triggered by Setting Change.");
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
            .ObserveOn(_uiContext) // Marshal result to UI thread
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

    // Filter logic remains the same
    private List<FilteredLogLine> ApplyIncrementalFilter(IList<(string LineText, long OriginalIndex)> buffer, IFilter filter)
    {
        return buffer
            .Select(item => new { Item = item, IsMatch = filter.IsMatch(item.LineText) })
            .Where(result => result.IsMatch)
            // Adjust OriginalLineNumber assignment based on TotalLines *before* this buffer
            // This requires careful handling of the TotalLinesSubject value or passing base index.
            // Simpler: Use the index passed in. It represents lines *since reset*.
            .Select(result => new FilteredLogLine((int)result.Item.OriginalIndex, result.Item.LineText))
            .ToList();
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
        // Store settings immediately for ApplyIncrementalFilter
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
