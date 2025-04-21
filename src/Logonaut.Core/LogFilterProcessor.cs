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
        private readonly Subject<(IFilter filter, int contextLines)> _filterSettingsSubject = new();
        private readonly CompositeDisposable _disposables = new();
        private readonly IScheduler _backgroundScheduler;
        private readonly BehaviorSubject<long> _totalLinesSubject = new BehaviorSubject<long>(0);
        private long _currentLineIndex = 0; // Tracks original line numbers internally
        private IFilter _currentFilter = new TrueFilter(); // Cache the latest filter
        private int _currentContextLines = 0; // Cache the latest context lines

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
            _disposables.Add(_filterSettingsSubject);
            _disposables.Add(_totalLinesSubject);
            _disposables.Add(Disposable.Create(() => _logSubscription?.Dispose()));
        }

        private void InitializePipelines()
        {
            _logSubscription?.Dispose();
            // --- Incremental Filtering Pipeline ---
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
                .Select(buffer => ApplyIncrementalFilter(buffer.Select(item => (item.LineText, item.OriginalIndex)).ToList())) // Pass current filter explicitly
                .Where(filteredLines => filteredLines.Count > 0)
                .ObserveOn(_uiContext) // Marshal result to UI thread
                .Subscribe(
                    matchedLines => _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Append, matchedLines)),
                    ex => HandlePipelineError("Incremental Filtering Error", ex) // TODO: Improve error handling
                );

            // --- Full Re-Filtering Pipeline (Triggered by Filter Changes) ---
            var fullRefilterSubscription = _filterSettingsSubject
                .Do(settings => // Immediately update cached settings
                {
                    _currentFilter = settings.filter;
                    _currentContextLines = settings.contextLines;
                })
                .Throttle(_filterDebounceTime, _backgroundScheduler) // Debounce requests
                .Select(settings => ApplyFullFilter(settings.filter, settings.contextLines)) // Perform filtering on background
                .ObserveOn(_uiContext) // Marshal result to UI thread
                .Subscribe(
                    newFilteredLines => _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Replace, newFilteredLines)),
                    ex => HandlePipelineError("Full Re-Filtering Error", ex) // TODO: Improve error handling
                );

            _disposables.Add(fullRefilterSubscription);
        }

        private List<FilteredLogLine> ApplyIncrementalFilter(IList<(string LineText, long OriginalIndex)> buffer)
        {
            IFilter filter = _currentFilter;
            int context = _currentContextLines;

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
            _filterSettingsSubject.OnNext((newFilter ?? new TrueFilter(), Math.Max(0, contextLines)));
        }

        public void Reset()
        {
            // Clear document, reset counter, and push an empty update
            Interlocked.Exchange(ref _currentLineIndex, 0);
            _logDocument.Clear();
            _currentFilter = new TrueFilter(); // Reset to default
            _currentContextLines = 0;
            _totalLinesSubject.OnNext(0); // Reset line count
            // Push an empty Replace update immediately on the UI thread. Removed for now as it clears the IsBusyFiltering.
            // _uiContext.Post(_ => _filteredUpdatesSubject.OnNext(new FilteredUpdate(UpdateType.Replace, Array.Empty<FilteredLogLine>())), null);
            // Also trigger a (debounced) filter application with the reset state
            UpdateFilterSettings(_currentFilter, _currentContextLines);
        }


        private void HandlePipelineError(string contextMessage, Exception ex)
        {
            // Instead of throwing, push the error to the output subject
            System.Diagnostics.Debug.WriteLine($"{contextMessage}: {ex}"); // Keep logging
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
            if (!_isDisposed) // Add isDisposed check
            {
                _isDisposed = true; // Mark as disposed early
                _totalLinesSubject?.OnCompleted();
                // Explicitly complete the subject BEFORE disposing _disposables
                _filteredUpdatesSubject?.OnCompleted();

                _logSubscription?.Dispose(); // Dispose specific subscriptions first if needed
                _disposables.Dispose(); // Dispose subjects and remaining subscriptions

                // Avoid calling OnCompleted again inside subject's Dispose
                // _filteredUpdatesSubject?.Dispose(); // Dispose is handled by _disposables
            }
        }
    }
}