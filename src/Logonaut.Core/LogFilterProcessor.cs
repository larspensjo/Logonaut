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
    private readonly ILogSource _logSource;
    private readonly SynchronizationContext _uiContext;
    private readonly IScheduler _backgroundScheduler;

    private readonly BehaviorSubject<FilteredUpdateBase> _filteredUpdatesSubject = new(new ReplaceFilteredUpdate(Array.Empty<FilteredLogLine>()));
    private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0);
    private readonly CompositeDisposable _disposables = new();

    private long _currentLineIndex = 0; // Tracks original numbers for *new* lines post-initial load
    private IFilter _currentFilter = new TrueFilter();
    private int _currentContextLines = 0;
    private Subject<(IFilter filter, int contextLines)>? _filterChangeTriggerSubject;
    private IDisposable? _logSubscription;

    private bool _isInitialLoadInProgress = false;
    private readonly object _stateLock = new object();

    private readonly Action<string> _addLineToDocumentCallback;

    // Config Constants  for throttling, but not used currently.
    private const int LineBufferSize = 50;
    private readonly TimeSpan _lineBufferTimeSpan = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _filterDebounceTime = TimeSpan.FromMilliseconds(100);

    public IObservable<FilteredUpdateBase> FilteredUpdates => _filteredUpdatesSubject.AsObservable();
    public IObservable<long> TotalLinesProcessed => _totalLinesSubject.AsObservable();

    public LogFilterProcessor(
        ILogSource logSource, // Changed parameter type
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

        InitializePipelines();

        _disposables.Add(_filteredUpdatesSubject);
        _disposables.Add(_totalLinesSubject);
    }

    private void InitializePipelines()
    {
        _logSubscription?.Dispose(); // Clean up previous if any

        // Pipeline for new lines from logSource.
        // For NOW, it still triggers the full refilter path.
        _logSubscription = _logSource.LogLines
            .Select(line => {
                var newIndex = Interlocked.Increment(ref _currentLineIndex);
                _totalLinesSubject.OnNext(_totalLinesSubject.Value + 1);
                _addLineToDocumentCallback?.Invoke(line);
                return line;
            })
            .ObserveOn(_backgroundScheduler)
            .Where(_ => { lock (_stateLock) return !_isInitialLoadInProgress; })
            .Subscribe(
                lineText => {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: New line '{lineText.Substring(0, Math.Min(lineText.Length, 20))}...' detected from ILogSource. Triggering full refilter check.");
                    _filterChangeTriggerSubject?.OnNext((_currentFilter, _currentContextLines));
                },
                ex => HandlePipelineError("Log Source Processing Error", ex)
            );
        _disposables.Add(_logSubscription);

        var filterChangeTrigger = new Subject<(IFilter filter, int contextLines)>();
        _disposables.Add(filterChangeTrigger);
        _filterChangeTriggerSubject = filterChangeTrigger;

        // Full refilter pipeline (triggered by settings change OR new lines *for now*)
        var fullRefilterSubscription = filterChangeTrigger
            .ObserveOn(_backgroundScheduler)
            .Throttle(_filterDebounceTime, _backgroundScheduler)
            .Select(settingsTuple => {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Starting ApplyFullFilter triggered by Setting Change OR New Line.");
                    var result = ApplyFullFilter(settingsTuple.Item1, settingsTuple.Item2);
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Finished ApplyFullFilter. Got {result.Count} lines.");
                     bool wasInitial = false;
                    long docCount = 0;
                    lock(_stateLock) { wasInitial = _isInitialLoadInProgress; }
                    if (wasInitial)
                    {
                        docCount = _logDocument.Count;
                        _totalLinesSubject.OnNext(docCount);
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Updated TotalLinesSubject after initial filter: {docCount}");
                    }
                    return result;
                })
            .ObserveOn(_uiContext) // Switch to UI thread before Subscribe
            .Subscribe(
                newFilteredLines => { // Receives the IReadOnlyList<FilteredLogLine>
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Sending Replace Update to UI. Lines={newFilteredLines.Count}. _isInitialLoadInProgress(BeforeSend) = { _isInitialLoadInProgress}");
                    bool wasInitialLoad = false;
                    lock(_stateLock) { wasInitialLoad = _isInitialLoadInProgress; }

                    // *** CREATE and EMIT the specific ReplaceFilteredUpdate type ***
                    _filteredUpdatesSubject.OnNext(new ReplaceFilteredUpdate(newFilteredLines));

                    if (wasInitialLoad)
                    {
                        lock(_stateLock) { _isInitialLoadInProgress = false; }
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> LogFilterProcessor: Initial Load Filter Complete. _isInitialLoadInProgress=false");
                    }
                },
                ex => {
                    lock(_stateLock) { _isInitialLoadInProgress = false; }
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
            _isInitialLoadInProgress = true;
        }
        Interlocked.Exchange(ref _currentLineIndex, 0);
        // ViewModel manages LogDocument clearing via source interaction
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
        // Dispose _filterChangeTriggerSubject first if needed
        _filterChangeTriggerSubject?.OnCompleted();
        _filterChangeTriggerSubject?.Dispose();
        _filterChangeTriggerSubject = null;

        // Explicitly complete the output subjects BEFORE disposing subscriptions
        // Check if not null and not already disposed (good practice)
        if (_filteredUpdatesSubject != null && !_filteredUpdatesSubject.IsDisposed)
        {
            _filteredUpdatesSubject.OnCompleted();
            _filteredUpdatesSubject.Dispose();
        }

        if (_totalLinesSubject != null && !_totalLinesSubject.IsDisposed)
        {
            _totalLinesSubject.OnCompleted();
            _totalLinesSubject.Dispose();
        }

        // Disposes subscriptions (_logSubscription, fullRefilterSubscription)
        _disposables.Dispose();

        // IMPORTANT: Processor should NOT dispose the _logSource it received.
        // The owner (MainViewModel) is responsible for disposing the log source.
    }
}
