using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.Core.Commands; 
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; 

namespace Logonaut.UI.ViewModels;

public enum SourceType
{
    File,
    Pasted,
    Simulator,
    Snapshot 
}

public record SearchResult(int Offset, int Length);

/*
* ViewModel for a single tab, representing a log source and its view.
* It orchestrates the LogDataProcessor for data handling and manages UI-specific state
* like filtered lines, search results, and display headers.
*
* Manages activation/deactivation and applies filters based on an associated profile.
*/
public partial class TabViewModel : ObservableObject, IDisposable
{
    // --- Services & Context (Injected) ---
    private readonly ICommandExecutor _globalCommandExecutor; 
    private readonly SynchronizationContext _uiContext;
    private readonly IScheduler? _backgroundScheduler; // Passed to LogDataProcessor

    // --- Core Log State & Processing ---
    private readonly LogDataProcessor _processor; 
    private readonly CompositeDisposable _processorSubscriptions = new CompositeDisposable();
    private readonly HashSet<int> _existingOriginalLineNumbers = new HashSet<int>();

    // --- Fields for Snapshot/File Reset Logic ---
    private string? _originalFilePathBeforeSnapshot;

    public ILogSource? LogSourceExposeDeprecated => _processor.LogSource;
    // --- UI Bound Properties ---
    [ObservableProperty] private string _header; // User-editable name
    [ObservableProperty] private string _displayHeader; // Combines Header and activity status
    [ObservableProperty] private bool _isActive; // Is this the currently selected tab in the UI
    [ObservableProperty] private string _associatedFilterProfileName;
    [ObservableProperty] private DateTime? _lastActivityTimestamp;
    [ObservableProperty] private ObservableCollection<FilteredLogLine> _filteredLogLines = new();
    [ObservableProperty] private long _totalLogLines; // Updated by LogDataProcessor's event
    [ObservableProperty] private string? _sourceIdentifier; // File path or pasted content temp path 
    [ObservableProperty] private bool _isAutoScrollEnabled = true; // Default to true, will be updated by MainViewModel

    [ObservableProperty] private SourceType _sourceType; 
    public string? PastedContentStoragePath { get; set; } 
    [ObservableProperty] private bool _isModified; 

    [ObservableProperty] private ObservableCollection<IFilter> _filterHighlightModels = new();
    [ObservableProperty] private int _highlightedFilteredLineIndex = -1;
    [ObservableProperty] private int _highlightedOriginalLineNumber = -1;

    [NotifyCanExecuteChangedFor(nameof(JumpToLineCommand))]
    [ObservableProperty] private string _targetOriginalLineNumberInput = string.Empty;

    [ObservableProperty] private string? _jumpStatusMessage;
    [ObservableProperty] private bool _isJumpTargetInvalid;

    // Search State
    [NotifyCanExecuteChangedFor(nameof(PreviousSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextSearchCommand))]
    [ObservableProperty] private string _searchText = "";
    
    [NotifyCanExecuteChangedFor(nameof(PreviousSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextSearchCommand))]
    [ObservableProperty] private bool _isCaseSensitiveSearch = false;
    private List<SearchResult> _searchMatches = new();
    private int _currentSearchIndex = -1;
    [ObservableProperty] private ObservableCollection<SearchResult> _searchMarkers = new();
    [ObservableProperty] private int _currentMatchOffset = -1;
    [ObservableProperty] private int _currentMatchLength = 0;
    public string SearchStatusText => GenerateSearchStatusText();


    // Editor instance for this tab (set by the View)
    private ICSharpCode.AvalonEdit.TextEditor? _logEditorInstance;

    public ICommandExecutor CommandExecutor => _globalCommandExecutor;
    public event EventHandler? RequestCloseTab;
    public event EventHandler? RequestScrollToEnd;
    public event EventHandler<int>? RequestScrollToLineIndex;
    public event EventHandler? FilteredLinesUpdated;
    public event Action<TabViewModel /*snapshotTab*/, string /*restartedFilePath*/>? SourceRestartDetected;

    public static readonly object LoadingToken = new(); // Specific to tab loading
    public static readonly object FilteringToken = new(); // Specific to tab filtering
    public ObservableCollection<object> CurrentBusyStates { get; } = new();

    public TabViewModel(
        string initialHeader,
        string initialAssociatedProfileName,
        SourceType initialSourceType,
        string? initialSourceIdentifier,
        ILogSourceProvider sourceProvider, 
        ICommandExecutor globalCommandExecutor,
        SynchronizationContext uiContext,
        IScheduler? backgroundScheduler = null) 
    {
        _header = initialHeader;
        _displayHeader = initialHeader;
        _associatedFilterProfileName = initialAssociatedProfileName;
        SourceType = initialSourceType;
        _sourceIdentifier = initialSourceIdentifier;

        _globalCommandExecutor = globalCommandExecutor;
        _uiContext = uiContext;
        _backgroundScheduler = backgroundScheduler; // Stored to pass to processor if needed, though processor takes it directly

        _processor = new LogDataProcessor(sourceProvider, uiContext, backgroundScheduler);
        SubscribeToProcessorEvents();

        CloseTabCommand = new RelayCommand(() => RequestCloseTab?.Invoke(this, EventArgs.Empty));
        PreviousSearchCommand = new RelayCommand(ExecutePreviousSearch, CanExecuteSearch);
        NextSearchCommand = new RelayCommand(ExecuteNextSearch, CanExecuteSearch);
        JumpToLineCommand = new AsyncRelayCommand(ExecuteJumpToLine, CanExecuteJumpToLine);

        CurrentBusyStates.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsLoading));
    }

    public bool IsLoading
    {
        get
        {
            bool isLoading = CurrentBusyStates.Contains(LoadingToken) || CurrentBusyStates.Contains(FilteringToken);
            // Debug.WriteLine($"---> TabViewModel '{Header}' IsLoading_get: {isLoading}. Tokens: Loading={CurrentBusyStates.Contains(LoadingToken)}, Filtering={CurrentBusyStates.Contains(FilteringToken)}");
            return isLoading;
        }
    }

    /*
    * Activates the tab's log processing. This involves setting up the log source and
    * the reactive filtering stream via the LogDataProcessor. It ensures that the tab
    * begins receiving and filtering log lines according to its configuration.
    * 
    * This method is typically called when a tab becomes the active tab in the UI,
    * or when its underlying source (e.g., file path, simulator state) is defined or changes.
    */
    public async Task ActivateAsync(IEnumerable<FilterProfileViewModel> availableProfiles, int globalContextLines, bool globalHighlightTimestamps, bool globalShowLineNumbers, bool globalAutoScrollEnabled, Action? onSourceResetDetectedHandler /* This parameter is now effectively unused by this method, but kept for compatibility if MainViewModel still passes it directly */)
    {
        // Check if already active with the same source configuration to avoid redundant work.
        // This is a simple check; more sophisticated checks might involve comparing actual source instances if they were exposed.
        if (IsActive && _processor.CurrentSourceType == SourceType && _processor.CurrentSourceIdentifier == SourceIdentifier)
        {
            Debug.WriteLine($"---> TabViewModel '{Header}': Already active with same source. Re-applying filters if profile/context changed.");
            ApplyFiltersFromProfile(availableProfiles, globalContextLines); // Ensure filters are current based on potentially changed profile
            return;
        }

        Debug.WriteLine($"---> TabViewModel '{Header}': Activating. SourceType: {SourceType}, Identifier: '{SourceIdentifier}'");
        _uiContext.Post(_ => CurrentBusyStates.Add(LoadingToken), null);

        // If it's a File or Simulator source being (re)activated, clear previous LogDoc content from processor
        // and reset UI collections.
        if (SourceType == SourceType.File || SourceType == SourceType.Simulator)
        {
            _processor.LogDocDeprecated.Clear();      // Clear LogDoc in processor
            ResetUICollectionsAndState();   // Clear related UI collections and states in TabViewModel
        }
        // For Pasted type, LogDoc is managed by LoadPastedContent, and ResetUICollectionsAndState
        // would have been called there if it was a new paste.
        
        IsActive = true; 
        LastActivityTimestamp = null; 
        UpdateDisplayHeader();

        // Determine the filter and context to apply initially
        var activeProfileVM = availableProfiles.FirstOrDefault(p => p.Name == AssociatedFilterProfileName);
        IFilter initialFilter = activeProfileVM?.Model?.RootFilter ?? new TrueFilter();
        
        try
        {
            // Pass internal HandleSourceFileRestarted for File types
            Action? effectiveSourceResetHandler = null;
            if (SourceType == SourceType.File)
            {
                effectiveSourceResetHandler = this.HandleSourceFileRestarted;
            }
            // For other types like Simulator, we might still pass the original onSourceResetDetectedHandler if it's meaningful,
            // or null if not. Currently, LogTailer's reset is specific to files.
            // For Simulator, the original onSourceResetDetectedHandler might be null or not used.
            else if (SourceType == SourceType.Simulator)
            {
                 effectiveSourceResetHandler = onSourceResetDetectedHandler; // Keep passing original for simulator if it uses it
            }


            await _processor.ActivateAsync(SourceType, SourceIdentifier, initialFilter, globalContextLines, effectiveSourceResetHandler);
        }
        catch (Exception ex)
        {
            HandleActivationError($"Error activating data processor for tab '{Header}': {ex.Message}");
            return; 
        }
        
        IsAutoScrollEnabled = globalAutoScrollEnabled; // Apply global auto-scroll setting

        // Note: LoadingToken is expected to be removed by the stream update from the processor
        // (ReplaceFilteredUpdate with IsInitialLoadProcessingComplete = true) or by HandleActivationError.
        Debug.WriteLine($"---> TabViewModel '{Header}': Activation initiated. Processor will handle data loading and filtering.");
    }

    private void HandleSourceFileRestarted()
    {
        // This method is called by the LogDataProcessor (which gets it from FileLogSource/LogTailer)
        // when a file truncation is detected. It should already be on the correct thread or LogDataProcessor should handle it.
        // However, since we are modifying UI-bound properties, ensure it's marshaled to the UI thread.

        _uiContext.Post(_ =>
        {
            // Defensive check: ensure this logic only runs if the tab is indeed a File source.
            // It's possible, though unlikely, that the source type changes between the callback setup and invocation.
            if (this.SourceType != SourceType.File || string.IsNullOrEmpty(this.SourceIdentifier))
            {
                Debug.WriteLine($"WARN: TabViewModel '{Header}' - HandleSourceFileRestarted called, but current SourceType is {this.SourceType} or SourceIdentifier is null/empty. Aborting snapshot transition.");
                return;
            }

            Debug.WriteLine($"---> TabViewModel '{Header}': File reset detected for '{this.SourceIdentifier}'. Transitioning to snapshot state.");

            _originalFilePathBeforeSnapshot = this.SourceIdentifier; // Store the original file path

            // Update Header and other properties
            Header += $" (Snapshot @ {DateTime.Now:HH:mm:ss})"; // DisplayHeader will update due to OnPropertyChanged on Header

            // Deactivate current processing. This stops monitoring, sets IsActive=false, updates LastActivityTimestamp.
            // It's important to do this before changing SourceType/Identifier if DeactivateLogProcessing relies on them.
            // However, DeactivateLogProcessing primarily calls _processor.Deactivate, which doesn't critically depend on these
            // for just stopping.
            DeactivateLogProcessing(); // This will also update DisplayHeader based on new IsActive and LastActivityTimestamp

            SourceType = SourceType.Snapshot; // Change SourceType to reflect it's now a snapshot

            // Change SourceIdentifier to make this tab unique and prevent it from reloading the original file.
            // Also ensures if it's persisted, it's distinct.
            SourceIdentifier = $"{_originalFilePathBeforeSnapshot}_snapshot_{Guid.NewGuid()}";

            IsModified = true; // Mark as modified, indicating its LogDocument content (the snapshot) should be persisted.

            // Raise the event to notify MainViewModel (or other subscribers)
            // that the original file path needs to be re-monitored.
            SourceRestartDetected?.Invoke(this, _originalFilePathBeforeSnapshot!);

            Debug.WriteLine($"---> TabViewModel '{Header}': Transitioned to snapshot. New SourceType: {SourceType}, New SourceIdentifier: {SourceIdentifier}");

        }, null);
    }

    private void HandleActivationError(string errorMessage)
    {
        Debug.WriteLine($"!!! TabViewModel '{Header}': Activation Error - {errorMessage}");
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Remove(LoadingToken);
            CurrentBusyStates.Remove(FilteringToken);
            MessageBox.Show(errorMessage, "Tab Activation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }, null);
        IsActive = false; 
        UpdateDisplayHeader();
        // Consider further cleanup, like ensuring processor is also in a clean state or deactivated
        _processor.Deactivate(clearLogDocument: true); // Deactivate processor and clear its doc on error
    }

    /*
    * Deactivates the tab's log processing. This stops the log source from producing new lines
    * and releases resources associated with the filtering stream.
    * 
    * Typically called when a tab is no longer the active tab or is being closed.
    */
    public void DeactivateLogProcessing()
    {
        // Check if already effectively deactivated to prevent redundant calls or errors
        if (!IsActive && _processor.LogSource == null && _processor.ReactiveFilteredLogStream == null)
        {
                Debug.WriteLine($"---> TabViewModel '{Header}': Already inactive or processor not active. Skipping full Deactivate.");
                return;
        }
        Debug.WriteLine($"---> TabViewModel '{Header}': Deactivating.");
        IsActive = false; // Set immediately to reflect state
        
        _processor.Deactivate(clearLogDocument: false); // Delegate to processor, keep LogDoc by default

        LastActivityTimestamp = DateTime.Now;
        UpdateDisplayHeader();
        
        _uiContext.Post(_ => {
            CurrentBusyStates.Remove(LoadingToken);
            CurrentBusyStates.Remove(FilteringToken);
        }, null);

        Debug.WriteLine($"---> TabViewModel '{Header}': Deactivation complete.");
    }

    private void SubscribeToProcessorEvents()
    {
        _processorSubscriptions.Clear(); 

        _processorSubscriptions.Add(_processor.FilteredLogUpdates
            .ObserveOn(_uiContext) 
            .Subscribe(
                update => ApplyFilteredUpdateToThisTab(update),
                ex => HandleProcessorError("Log Processing Error in Tab", ex)
            ));

        _processorSubscriptions.Add(_processor.TotalLinesProcessed
            .Sample(TimeSpan.FromMilliseconds(200), _backgroundScheduler ?? Scheduler.Default) 
            .ObserveOn(_uiContext) 
            .Subscribe(
                count => TotalLogLines = count,
                ex => HandleProcessorError("Total Lines Error in Tab", ex)
            ));
    }

    /*
    * Processes updates to the filtered log lines received from the LogDataProcessor.
    * It updates the UI-bound collection (FilteredLogLines), manages the underlying
    * AvalonEdit document content, and handles selection restoration and auto-scrolling.
    * 
    * This method distinguishes between appending new lines and replacing the entire
    * filtered view, performing the necessary operations for each case.
    */
    private void ApplyFilteredUpdateToThisTab(FilteredUpdateBase update)
    {
        bool wasLoadingTokenPresent = CurrentBusyStates.Contains(LoadingToken);
        _uiContext.Post(_ => { if (!CurrentBusyStates.Contains(FilteringToken)) CurrentBusyStates.Add(FilteringToken); }, null);
        
        bool newLinesWereAppended = false;
        bool structuralChangeOccurred = false;
        int previousHighlightedOriginalLine = HighlightedOriginalLineNumber; 

        if (update is AppendFilteredUpdate appendUpdate)
        {
            var linesActuallyAdded = new List<FilteredLogLine>();
            foreach (var lineToAdd in appendUpdate.Lines)
            {
                if (_existingOriginalLineNumbers.Add(lineToAdd.OriginalLineNumber))
                {
                    FilteredLogLines.Add(lineToAdd);
                    linesActuallyAdded.Add(lineToAdd); 
                }
                // Else: If line with same OriginalLineNumber already exists, we might need to update it
                // if its IsContextLine status changed. For simplicity in append, this is often ignored,
                // assuming appends are for new lines not previously seen or whose context status won't flip.
                // A more robust merge would be needed if appends could change existing lines' context status.
            }
            if (linesActuallyAdded.Any())
            {
                newLinesWereAppended = true;
                structuralChangeOccurred = true; 
                ScheduleLogTextAppend(linesActuallyAdded); 
            }
        }
        else if (update is ReplaceFilteredUpdate replaceUpdate)
        {
            _existingOriginalLineNumbers.Clear();
            FilteredLogLines.Clear(); 
            foreach (var line in replaceUpdate.Lines)
            {
                FilteredLogLines.Add(line);
                _existingOriginalLineNumbers.Add(line.OriginalLineNumber);
            }
            structuralChangeOccurred = true; 
            ScheduleLogTextUpdate(FilteredLogLines); 

            // Restore selection if possible
            if (previousHighlightedOriginalLine > 0)
            {
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == previousHighlightedOriginalLine)?.Index ?? -1;
                
                // Setting HighlightedFilteredLineIndex will trigger OnHighlightedFilteredLineIndexChanged,
                // which in turn sets HighlightedOriginalLineNumber.
                _uiContext.Post(idx => { HighlightedFilteredLineIndex = (int)idx!; }, newIndex);
                if (newIndex != -1) RequestScrollToLineIndex?.Invoke(this, newIndex);
            }
            else
            {
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            if (replaceUpdate.IsInitialLoadProcessingComplete && wasLoadingTokenPresent)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} TabVM ApplyFilteredUpdate: Initial Load Processing Complete for tab '{Header}'.");
                _uiContext.Post(_ => CurrentBusyStates.Remove(LoadingToken), null);
            }
        }
        
        OnPropertyChanged(nameof(FilteredLogLinesCount)); // Notify FilteredLogLinesCount changed

        if (structuralChangeOccurred)
        {
            FilteredLinesUpdated?.Invoke(this, EventArgs.Empty); // Raise the event
        }

        if (newLinesWereAppended && IsAutoScrollEnabled)
        {
            RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
        }
        // Remove FilteringToken after all processing for this update is done
        _uiContext.Post(_ => CurrentBusyStates.Remove(FilteringToken), null); 
    }

    public int FilteredLogLinesCount => FilteredLogLines.Count;

    private void HandleProcessorError(string contextMessage, Exception ex)
    {
        Debug.WriteLine($"!!! TabViewModel '{Header}' - {contextMessage}: {ex}");
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Remove(LoadingToken); 
            CurrentBusyStates.Remove(FilteringToken);
            MessageBox.Show($"Error in tab '{Header}': {ex.Message}", "Tab Error", MessageBoxButton.OK, MessageBoxImage.Error);
            DeactivateLogProcessing(); 
        }, null);
    }

    public void LoadPastedContent(string text)
    {
        if (SourceType != SourceType.Pasted)
            throw new InvalidOperationException("LoadPastedContent can only be called on PastedSourceType tabs.");
        
        _processor.LoadPastedLogContent(text); 
        ResetUICollectionsAndState();          
        TotalLogLines = _processor.LogDocDeprecated.Count; 
        IsModified = true;
        // MainViewModel will call ActivateAsync after this to apply filters to the newly pasted content.
    }
    
    private void ResetUICollectionsAndState()
    {
        _existingOriginalLineNumbers.Clear();
        FilteredLogLines.Clear();
        ScheduleLogTextUpdate(FilteredLogLines); 
        // TotalLogLines is now managed by the processor or set after processor actions.
        ResetSearchState();
        HighlightedFilteredLineIndex = -1;
        HighlightedOriginalLineNumber = -1;
        TargetOriginalLineNumberInput = string.Empty;
        JumpStatusMessage = string.Empty;
        IsJumpTargetInvalid = false;
    }

    public void SetLogEditorInstance(ICSharpCode.AvalonEdit.TextEditor editor)
    {
        _logEditorInstance = editor;
        if (IsActive && FilteredLogLines.Any()) 
        {
            ScheduleLogTextUpdate(FilteredLogLines);
        }
    }

    private void ScheduleLogTextAppend(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        var linesSnapshot = linesToAppend.ToList(); // Ensure thread safety for the collection
        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!;
            if (_logEditorInstance?.Document != null) AppendLogTextInternal(lines);
            UpdateSearchMatches(); // Search matches need to be updated after text changes
        }, linesSnapshot);
    }

    private void AppendLogTextInternal(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        if (_logEditorInstance?.Document == null || !linesToAppend.Any()) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            bool needsLeadingNewline = _logEditorInstance.Document.TextLength > 0 &&
                                        _logEditorInstance.Document.GetCharAt(_logEditorInstance.Document.TextLength - 1) != '\n';
            for (int i = 0; i < linesToAppend.Count; i++)
            {
                if (needsLeadingNewline || i > 0) sb.Append(Environment.NewLine);
                sb.Append(linesToAppend[i].Text);
            }
            _logEditorInstance.Document.BeginUpdate();
            _logEditorInstance.Document.Insert(_logEditorInstance.Document.TextLength, sb.ToString());
            _logEditorInstance.Document.EndUpdate();
        }
        catch (Exception ex) { Debug.WriteLine($"Error appending text to Tab's AvalonEdit: {ex.Message}"); }
    }

    private void ScheduleLogTextUpdate(IReadOnlyList<FilteredLogLine> relevantLines)
    {
        var linesSnapshot = relevantLines.ToList(); // Ensure thread safety
        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!;
            if (_logEditorInstance?.Document != null) ReplaceLogTextInternal(lines);
            UpdateSearchMatches(); // Search matches need to be updated after text changes
        }, linesSnapshot);
    }

    private void ReplaceLogTextInternal(IReadOnlyList<FilteredLogLine> allLines)
    {
        if (_logEditorInstance?.Document == null) return;
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var line in allLines)
        {
            if (!first) { sb.Append(Environment.NewLine); }
            sb.Append(line.Text);
            first = false;
        }
        _logEditorInstance.Document.Text = sb.ToString();
    }

    partial void OnHeaderChanged(string value) => UpdateDisplayHeader();
    partial void OnLastActivityTimestampChanged(DateTime? value) => UpdateDisplayHeader();
    partial void OnIsActiveChanged(bool value) => UpdateDisplayHeader();

    private void UpdateDisplayHeader()
    {
        if (IsActive || LastActivityTimestamp == null) DisplayHeader = Header;
        else DisplayHeader = $"{Header} (Paused @ {LastActivityTimestamp:HH:mm:ss})";
    }

    public void ApplyFiltersFromProfile(IEnumerable<FilterProfileViewModel> availableProfiles, int globalContextLines)
    {
        if (!IsActive) 
        {
            Debug.WriteLine($"---> TabViewModel '{Header}': ApplyFiltersFromProfile called but tab is not active. Skipping.");
            return;
        }
        if (_processor == null) // Should not happen if constructor ran
        {
            Debug.WriteLine($"---> TabViewModel '{Header}': ApplyFiltersFromProfile called but processor is null. Skipping.");
            return;
        }

        var profileVM = availableProfiles.FirstOrDefault(p => p.Name == AssociatedFilterProfileName);
        IFilter? filterToApply = profileVM?.Model?.RootFilter ?? new TrueFilter();

        Debug.WriteLine($"---> TabViewModel '{Header}': Applying filters. Profile: '{AssociatedFilterProfileName}', Filter: '{filterToApply.GetType().Name}', Context: {globalContextLines}");
        _uiContext.Post(_ => { if (!CurrentBusyStates.Contains(FilteringToken)) CurrentBusyStates.Add(FilteringToken); }, null);
        
        _processor.ApplyFilterSettings(filterToApply, globalContextLines); 
        UpdateFilterHighlightModels(filterToApply); 
    }

    private void UpdateFilterHighlightModels(IFilter? rootFilter)
    {
        var newFilterModels = new ObservableCollection<IFilter>();
        if (rootFilter != null) TraverseFilterTreeForHighlightingRecursive(rootFilter, newFilterModels);
        FilterHighlightModels = newFilterModels; // This will notify AvalonEditHelper via binding
    }

    private void TraverseFilterTreeForHighlightingRecursive(IFilter filter, ObservableCollection<IFilter> models)
    {
        if (!filter.Enabled) return;
        // Only add filters that have a value and are of a type that we highlight based on value
        if ((filter is SubstringFilter || filter is RegexFilter) && !string.IsNullOrEmpty(filter.Value))
        {
            if (!models.Contains(filter)) models.Add(filter);
        }
        if (filter is CompositeFilter composite)
        {
            foreach (var child in composite.SubFilters) TraverseFilterTreeForHighlightingRecursive(child, models);
        }
    }

    public IRelayCommand CloseTabCommand { get; }

    // --- Search Logic ---
    public IRelayCommand PreviousSearchCommand { get; }
    public IRelayCommand NextSearchCommand { get; }
    private void ExecutePreviousSearch()
    {
        if (_searchMatches.Count == 0) return;
        if (_currentSearchIndex == -1) _currentSearchIndex = _searchMatches.Count - 1; // Wrap to end if starting
        else _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        SelectAndScrollToCurrentMatch();
    }
    private void ExecuteNextSearch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatches.Count;
        SelectAndScrollToCurrentMatch();
    }
    private bool CanExecuteSearch() => !string.IsNullOrWhiteSpace(SearchText) && FilteredLogLines.Any();

    private void SelectAndScrollToCurrentMatch()
    {
        if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchMatches.Count)
        {
            var match = _searchMatches[_currentSearchIndex];
            CurrentMatchOffset = match.Offset;
            CurrentMatchLength = match.Length;
            int lineIndex = FindFilteredLineIndexContainingOffset(CurrentMatchOffset);
            HighlightedFilteredLineIndex = lineIndex; // This will trigger OnHighlightedFilteredLineIndexChanged
            if(lineIndex != -1) RequestScrollToLineIndex?.Invoke(this, lineIndex);
        }
        else
        {
            CurrentMatchOffset = -1; CurrentMatchLength = 0; HighlightedFilteredLineIndex = -1;
        }
        OnPropertyChanged(nameof(SearchStatusText)); // Update status text
    }

    partial void OnSearchTextChanged(string value) => UpdateSearchMatches();
    partial void OnIsCaseSensitiveSearchChanged(bool value) => UpdateSearchMatches();

    private string GetCurrentDocumentTextForSearch()
    {
        // Prefer editor's live text if available, otherwise reconstruct from FilteredLogLines
        if (_logEditorInstance?.Document != null) return _logEditorInstance.Document.Text;
        
        // Fallback: reconstruct from FilteredLogLines. This ensures search works even if editor isn't fully ready.
        return FilteredLogLines.Any() ? string.Join(Environment.NewLine, FilteredLogLines.Select(fll => fll.Text)) : string.Empty;
    }

    private void UpdateSearchMatches()
    {
        string currentSearchTerm = SearchText; 
        string textToSearch = GetCurrentDocumentTextForSearch();
        ResetSearchState(); 

        if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
        {
            OnPropertyChanged(nameof(SearchStatusText)); // Ensure status is cleared or updated
            return;
        }

        int offset = 0;
        var stringComparison = IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var tempMarkers = new List<SearchResult>(); // Build locally then update observable
        while (offset < textToSearch.Length)
        {
            int foundIndex = textToSearch.IndexOf(currentSearchTerm, offset, stringComparison);
            if (foundIndex == -1) break;
            var newMatch = new SearchResult(foundIndex, currentSearchTerm.Length);
            _searchMatches.Add(newMatch);
            tempMarkers.Add(newMatch); // For batch update to SearchMarkers
            offset = foundIndex + currentSearchTerm.Length;
        }
        
        // Batch update SearchMarkers for performance
        foreach (var marker in tempMarkers) SearchMarkers.Add(marker);
        
        if (_searchMatches.Any()) _currentSearchIndex = 0; // Select first match if any
        SelectAndScrollToCurrentMatch(); // This also updates SearchStatusText via OnPropertyChanged
    }
    
    private void ResetSearchState()
    {
        _searchMatches.Clear();
        SearchMarkers.Clear(); 
        _currentSearchIndex = -1;
        // CurrentMatchOffset and CurrentMatchLength are reset by SelectAndScrollToCurrentMatch
        // when no match is selected after reset.
        CurrentMatchOffset = -1;
        CurrentMatchLength = 0;
        // HighlightedFilteredLineIndex = -1; // Optionally clear selection too
        OnPropertyChanged(nameof(SearchStatusText)); // Ensure status text updates
    }

    private int FindFilteredLineIndexContainingOffset(int charOffset)
    {
        int currentCumulativeOffset = 0;
        for (int i = 0; i < FilteredLogLines.Count; i++)
        {
            var line = FilteredLogLines[i];
            // End offset is start + length. The character at 'lineEndOffset' is the newline or EOF.
            int lineEndOffset = currentCumulativeOffset + line.Text.Length; 
            
            // If charOffset is exactly at currentCumulativeOffset, it's on this line.
            // If charOffset is between currentCumulativeOffset and lineEndOffset-1, it's on this line.
            // If charOffset is lineEndOffset, it's considered at the end of this line (before newline).
            if (charOffset >= currentCumulativeOffset && charOffset <= lineEndOffset)
            {
                // Special case for zero-length match at the very end of the document, on the last line
                if (charOffset == lineEndOffset && charOffset == _logEditorInstance?.Document?.TextLength && i == FilteredLogLines.Count -1) {
                        return i;
                }
                // If match is at lineEndOffset but it's not the end of the document, it means it's effectively at the start of the *next* line's content if newlines are counted.
                // However, IndexOf gives the start of the match. So if a match starts at lineEndOffset, it means it's starting on the *next* visual line.
                // This logic needs to be careful. Let's assume the offset is within the *text content* of the line.
                if (charOffset < lineEndOffset || (charOffset == lineEndOffset && line.Text.Length == 0) ) // if it's within the line, or it's an empty line and match is at its start
                        return i;
            }
            currentCumulativeOffset = lineEndOffset + Environment.NewLine.Length; // Account for newline
        }
        return -1; // Offset not found within any line
    }

    private string GenerateSearchStatusText()
    {
        if (string.IsNullOrEmpty(SearchText) || !FilteredLogLines.Any()) return "";
        if (_searchMatches.Count == 0) return "Phrase not found";
        if (_currentSearchIndex == -1 && _searchMatches.Any()) return $"{_searchMatches.Count} matches found"; 
        return $"Match {_currentSearchIndex + 1} of {_searchMatches.Count}";
    }

    // --- Jump To Line Logic ---
    public IAsyncRelayCommand JumpToLineCommand { get; }
    private async Task ExecuteJumpToLine()
    {
        IsJumpTargetInvalid = false; JumpStatusMessage = string.Empty;
        if (!int.TryParse(TargetOriginalLineNumberInput, out int targetLineNumber) || targetLineNumber <= 0)
        {
            JumpStatusMessage = "Invalid line number.";
            await TriggerInvalidInputFeedback();
            return;
        }
        var foundLine = FilteredLogLines.Select((line, index) => new { line.OriginalLineNumber, Index = index })
                                        .FirstOrDefault(item => item.OriginalLineNumber == targetLineNumber);
        if (foundLine != null)
        {
            HighlightedFilteredLineIndex = foundLine.Index; // This will trigger selection and scroll
            RequestScrollToLineIndex?.Invoke(this, foundLine.Index);
        }
        else
        {
            JumpStatusMessage = $"Line {targetLineNumber} not found in filtered view.";
            await TriggerInvalidInputFeedback();
        }
    }

    private bool CanExecuteJumpToLine() => !string.IsNullOrWhiteSpace(TargetOriginalLineNumberInput) && FilteredLogLines.Any();
    private async Task TriggerInvalidInputFeedback()
    {
        IsJumpTargetInvalid = true;
        await Task.Delay(2500); 
        if (IsJumpTargetInvalid) 
        {
            IsJumpTargetInvalid = false;
            JumpStatusMessage = string.Empty;
        }
    }

    partial void OnHighlightedFilteredLineIndexChanged(int oldValue, int newValue)
    {
        if (newValue >= 0 && newValue < FilteredLogLines.Count)
        {
            HighlightedOriginalLineNumber = FilteredLogLines[newValue].OriginalLineNumber;
            Debug.WriteLine($"TabVM: HighlightedFilteredLineIndex changed to {newValue}, Original: {FilteredLogLines[newValue].OriginalLineNumber}");
        }
        else
        {
            HighlightedOriginalLineNumber = -1; // Ensure original is also reset if index is invalid
            Debug.WriteLine($"TabVM: HighlightedFilteredLineIndex changed to {newValue} (invalid), Original set to -1");
        }
    }

    partial void OnHighlightedOriginalLineNumberChanged(int oldValue, int newValue)
    {
        if (!_isEditingTargetOriginalLineNumber) 
        {
                TargetOriginalLineNumberInput = (newValue > 0) ? newValue.ToString() : string.Empty;
        }
        Debug.WriteLine($"TabVM: HighlightedOriginalLineNumber changed to {newValue}. Input box updated: {TargetOriginalLineNumberInput}");
    }

    private bool _isEditingTargetOriginalLineNumber = false;
    partial void OnTargetOriginalLineNumberInputChanging(string? oldValue, string newValue) => _isEditingTargetOriginalLineNumber = true;
    partial void OnTargetOriginalLineNumberInputChanged(string value) => _isEditingTargetOriginalLineNumber = false;


    private bool _disposed = false;
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            Debug.WriteLine($"---> TabViewModel '{Header}': Disposing.");
            DeactivateLogProcessing(); // Ensure processor is deactivated and resources released
            _processor?.Dispose(); // Dispose the processor
            _processorSubscriptions.Dispose();
            CurrentBusyStates.Clear(); 
        }
        _disposed = true;
    }
}

// Simple NullLogSource for pasted content or other non-streaming scenarios
public class NullLogSource : ILogSource
{
    public IObservable<string> LogLines => Observable.Empty<string>();
    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback) => Task.FromResult(0L);
    public void StartMonitoring(Action? onLogFileResetDetected) { }
    public void StopMonitoring() { }
    public void Dispose() { }
}
