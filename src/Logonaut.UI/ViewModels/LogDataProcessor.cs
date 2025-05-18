using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics; // For Debug

namespace Logonaut.UI.ViewModels;
/*
* Manages the lifecycle and processing of log data for a single log view context (e.g., a tab).
* This includes owning the raw log document, interacting with the log source (file, simulator, etc.),
* and managing the reactive stream that applies filters to produce the viewable log lines.
*
* It encapsulates the complexities of log data acquisition and filtering, providing observables
* for filtered updates and total line counts to its owner (typically a TabViewModel).
*/
public class LogDataProcessor : IDisposable
{
    private readonly ILogSourceProvider _sourceProvider;
    private readonly SynchronizationContext _uiContext;
    private readonly IScheduler? _backgroundScheduler;
    private CompositeDisposable _streamSubscriptions = new CompositeDisposable();

    private readonly Subject<FilteredUpdateBase> _filteredUpdatesOutSubject = new Subject<FilteredUpdateBase>();
    private readonly Subject<long> _totalLinesOutSubject = new Subject<long>();

    private readonly LogDocument _logDocument = new LogDocument();
    public LogDocument LogDocDeprecated => _logDocument;
    public ILogSource? LogSource { get; private set; }
    public IReactiveFilteredLogStream? ReactiveFilteredLogStream { get; private set; }

    public IObservable<FilteredUpdateBase> FilteredLogUpdates => _filteredUpdatesOutSubject.AsObservable();
    public IObservable<long> TotalLinesProcessed => _totalLinesOutSubject.AsObservable();

    public SourceType CurrentSourceType { get; private set; } = SourceType.Pasted; // Default
    public string? CurrentSourceIdentifier { get; private set; }


    public LogDataProcessor(
        ILogSourceProvider sourceProvider,
        SynchronizationContext uiContext,
        IScheduler? backgroundScheduler = null)
    {
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
        _backgroundScheduler = backgroundScheduler; // Can be null, ReactiveFilteredLogStream handles default
    }

    /*
    * Initializes or re-initializes the log source and reactive filtering stream based on the provided
    * source type and identifier. It prepares the source, subscribes to updates, and handles
    * the initial loading of log lines.
    */
    public async Task ActivateAsync(SourceType sourceType, string? sourceIdentifier, IFilter initialFilter, int initialContextLines)
    {
        Debug.WriteLine($"---> LogDataProcessor: Activating. SourceType: {sourceType}, Identifier: '{sourceIdentifier}'");
        CurrentSourceType = sourceType;
        CurrentSourceIdentifier = sourceIdentifier;

        // Dispose existing resources before creating new ones
        DeactivateInternal(clearLogDoc: false); // Keep LogDoc unless explicitly cleared elsewhere

        try
        {
            LogSource?.Dispose(); // Ensure previous source is disposed
            switch (sourceType)
            {
                case SourceType.File: LogSource = _sourceProvider.CreateFileLogSource(); break;
                case SourceType.Simulator: LogSource = _sourceProvider.CreateSimulatorLogSource(); break;
                case SourceType.Pasted: LogSource = new NullLogSource(); break; // For pasted, source is inert
                default: throw new InvalidOperationException($"Unsupported SourceType: {sourceType}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"!!! LogDataProcessor: Error initializing log source: {ex.Message}");
            // Propagate or handle error appropriately
            throw; // Re-throw for the caller (TabViewModel) to handle UI feedback
        }

        ReactiveFilteredLogStream?.Dispose(); // Ensure previous stream is disposed
        ReactiveFilteredLogStream = new ReactiveFilteredLogStream(LogSource, _logDocument, _uiContext, AddLineToLogDocumentCallback, _backgroundScheduler);
        SubscribeToFilteredStreamEvents(); // Subscribe to the processor's outputs
        ReactiveFilteredLogStream.Reset(); // Reset stream state for new source/filter

        long initialLinesLoaded = 0;
        if (sourceType == SourceType.File && !string.IsNullOrEmpty(sourceIdentifier))
        {
            // LogDoc is cleared by TabViewModel before calling Activate on processor for File/Simulator
            try
            {
                initialLinesLoaded = await LogSource.PrepareAndGetInitialLinesAsync(sourceIdentifier, AddLineToLogDocumentCallback).ConfigureAwait(true);
                _uiContext.Post(count => _totalLinesOutSubject.OnNext((long)count!), initialLinesLoaded);
                LogSource.StartMonitoring();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! LogDataProcessor: Error preparing file source: {ex.Message}");
                DeactivateInternal(clearLogDoc: true); // Cleanup on error
                throw; // Re-throw
            }
        }
        else if (sourceType == SourceType.Simulator && !string.IsNullOrEmpty(sourceIdentifier))
        {
            // LogDoc is cleared by TabViewModel
            initialLinesLoaded = await LogSource.PrepareAndGetInitialLinesAsync(sourceIdentifier, AddLineToLogDocumentCallback).ConfigureAwait(false);
            _uiContext.Post(count => _totalLinesOutSubject.OnNext((long)count!), initialLinesLoaded); // Usually 0 for simulator start
            LogSource.StartMonitoring();
        }
        else if (sourceType == SourceType.Pasted)
        {
            // For pasted content, LogDoc is assumed to be populated by TabViewModel before calling Activate.
            // The ReactiveFilteredLogStream will process the existing content of LogDoc.
            initialLinesLoaded = _logDocument.Count;
            _uiContext.Post(count => _totalLinesOutSubject.OnNext((long)count!), initialLinesLoaded);
        }

        // Apply initial filter settings
        ApplyFilterSettings(initialFilter, initialContextLines);
        Debug.WriteLine($"---> LogDataProcessor: Activation complete. Initial lines reported: {initialLinesLoaded}");
    }

    /*
    * Stops monitoring the log source and disposes of the reactive stream and source resources.
    * Optionally clears the internal log document.
    */
    public void Deactivate(bool clearLogDocument = false)
    {
        Debug.WriteLine($"---> LogDataProcessor: Deactivating. Clear LogDoc: {clearLogDocument}");
        DeactivateInternal(clearLogDocument);
    }

    private void DeactivateInternal(bool clearLogDoc)
    {
        LogSource?.StopMonitoring();

        // Unsubscribe from previous stream's events before disposing it
        _streamSubscriptions.Dispose();
        _streamSubscriptions = new CompositeDisposable(); // Recreate for next activation

        ReactiveFilteredLogStream?.Dispose();
        ReactiveFilteredLogStream = null;

        LogSource?.Dispose();
        LogSource = null;

        if (clearLogDoc)
        {
            _logDocument.Clear();
            _uiContext.Post(_ => _totalLinesOutSubject.OnNext(0), null); // Reset total lines if doc cleared
        }
        Debug.WriteLine($"---> LogDataProcessor: DeactivationInternal complete.");
    }

    private void AddLineToLogDocumentCallback(string line)
    {
        _logDocument.AppendLine(line);
        // Note: TotalLinesProcessed is updated by the ReactiveFilteredLogStream itself internally
        // based on lines it receives from ILogSource.LogLines, not directly from this callback.
        // This callback is primarily for initial lines from PrepareAndGetInitialLinesAsync.
    }

    private void SubscribeToFilteredStreamEvents()
    {
        if (ReactiveFilteredLogStream == null) return;

        _streamSubscriptions.Add(ReactiveFilteredLogStream.FilteredUpdates
            .ObserveOn(_uiContext) // Ensure observer runs on UI thread if it modifies UI-bound collections
            .Subscribe(
                update => _filteredUpdatesOutSubject.OnNext(update),
                ex => _filteredUpdatesOutSubject.OnError(ex) // Propagate errors
            ));

        _streamSubscriptions.Add(ReactiveFilteredLogStream.TotalLinesProcessed
            // No ObserveOn needed here if TabViewModel handles UI thread switching
            .Subscribe(
                count => _totalLinesOutSubject.OnNext(count),
                ex => _totalLinesOutSubject.OnError(ex) // Propagate errors
            ));
    }

    /*
        * Applies new filter settings to the currently active reactive stream.
        */
    public void ApplyFilterSettings(IFilter newFilter, int contextLines)
    {
        if (ReactiveFilteredLogStream == null)
        {
            Debug.WriteLine($"---> LogDataProcessor: ApplyFilterSettings called but ReactiveFilteredLogStream is null. Source: {CurrentSourceType} {CurrentSourceIdentifier}");
            return;
        }
        Debug.WriteLine($"---> LogDataProcessor: Applying filter settings. Filter: '{newFilter.GetType().Name}', Context: {contextLines}");
        ReactiveFilteredLogStream.UpdateFilterSettings(newFilter, contextLines);
    }

    /*
    * Populates the LogDocument with initial lines, typically used for pasted content.
    * This should be called before ActivateAsync for pasted content if LogDoc isn't already populated.
    */
    public void LoadPastedLogContent(string text)
    {
        _logDocument.Clear(); // Clear previous content before adding new
        _logDocument.AddInitialLines(text);
        _uiContext.Post(_ => _totalLinesOutSubject.OnNext(_logDocument.Count), null);
    }


    private bool _disposed = false;
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            Debug.WriteLine($"---> LogDataProcessor: Disposing.");
            DeactivateInternal(clearLogDoc: false); // Deactivate fully

            _filteredUpdatesOutSubject.OnCompleted();
            _filteredUpdatesOutSubject.Dispose();
            _totalLinesOutSubject.OnCompleted();
            _totalLinesOutSubject.Dispose();

            _streamSubscriptions.Dispose();
        }
        _disposed = true;
    }
}
