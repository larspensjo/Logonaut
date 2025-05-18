using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Commands; // For ICommandExecutor
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
using System.Windows; // For MessageBox

namespace Logonaut.UI.ViewModels;

public enum SourceType
{
    File,
    Pasted,
    Simulator
}

public record SearchResult(int Offset, int Length);

/*
 * ViewModel for a single tab, representing a log source and its view.
 *
 * Purpose:
 * Manages the state and logic for an individual log session, including its
 * log document, source, filter processor, and UI-related properties like
 * filtered lines, search results, and display headers.
 *
 * Role:
 * Encapsulates all data and operations specific to one log tab, allowing
 * MainViewModel to manage a collection of these for a tabbed interface.
 * Handles activation/deactivation, loading data, applying filters, and searching
 * within its own context.
 */
public partial class TabViewModel : ObservableObject, IDisposable, ICommandExecutorProvider
{
    // --- Fields and Properties remain largely the same ---
    private readonly ILogSourceProvider _sourceProvider;
    private readonly ICommandExecutor _globalCommandExecutor;
    private readonly SynchronizationContext _uiContext;
    private readonly IScheduler? _backgroundScheduler;

    public LogDocument LogDoc { get; } = new();
    public ILogSource? LogSource { get; private set; }
    public IReactiveFilteredLogStream? ReactiveFilteredLogStream { get; private set; }
    private readonly CompositeDisposable _streamSubscriptions = new();
    private readonly HashSet<int> _existingOriginalLineNumbers = new HashSet<int>();


    // --- UI Bound Properties ---
    [ObservableProperty] private string _header; // User-editable name
    [ObservableProperty] private string _displayHeader; // Combines Header and activity status
    [ObservableProperty] private bool _isActive; // Is this the currently selected tab in the UI
    [ObservableProperty] private string _associatedFilterProfileName;
    [ObservableProperty] private DateTime? _lastActivityTimestamp;
    [ObservableProperty] private ObservableCollection<FilteredLogLine> _filteredLogLines = new();
    [ObservableProperty] private long _totalLogLines;
    [ObservableProperty] private string? _sourceIdentifier; // File path or pasted content temp path
    [ObservableProperty] private bool _isAutoScrollEnabled = true; // Default to true, will be updated by MainViewModel

    public SourceType SourceType
    {
        get;
        set; // TODO: [Phase 1 Cleanup] Re-evaluate SourceType mutability once multiple tabs are distinct instances.
    }
    public string? PastedContentStoragePath { get; set; }
    [ObservableProperty] private bool _isModified;

    // Highlighting and Selection
    [ObservableProperty] private ObservableCollection<IFilter> _filterHighlightModels = new();
    [ObservableProperty] private int _highlightedFilteredLineIndex = -1;
    [ObservableProperty] private int _highlightedOriginalLineNumber = -1;

    // Jump To Line
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

        _sourceProvider = sourceProvider;
        _globalCommandExecutor = globalCommandExecutor;
        _uiContext = uiContext;
        _backgroundScheduler = backgroundScheduler;

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

    public async Task ActivateAsync(IEnumerable<FilterProfileViewModel> availableProfiles, int globalContextLines, bool globalHighlightTimestamps, bool globalShowLineNumbers, bool globalAutoScrollEnabled)
    {
        if (IsActive)
        {
            // If already active, perhaps just re-apply filters if profile/context changed.
            // For now, a full reactivation might be simpler if parameters like profile have changed.
            // However, the plan implies Activate is for when a tab *becomes* active.
            // If it's already active and settings change, ApplyFiltersFromProfile is called.
            // This ActivateAsync is primarily for initial activation or re-activation after Deactivate.
            Debug.WriteLine($"---> TabViewModel '{Header}': Already active. Skipping full ActivateAsync. Re-applying filters.");
            ApplyFiltersFromProfile(availableProfiles, globalContextLines);
            return;
        }


        Debug.WriteLine($"---> TabViewModel '{Header}': Activating. SourceType: {SourceType}, Identifier: '{SourceIdentifier}'");
        _uiContext.Post(_ => CurrentBusyStates.Add(LoadingToken), null);
        IsActive = true;
        LastActivityTimestamp = null;
        UpdateDisplayHeader();

        try
        {
            LogSource?.Dispose();
            switch (SourceType)
            {
                case SourceType.File: LogSource = _sourceProvider.CreateFileLogSource(); break;
                case SourceType.Simulator: LogSource = _sourceProvider.CreateSimulatorLogSource(); break;
                case SourceType.Pasted: LogSource = new NullLogSource(); break;
                default: throw new InvalidOperationException($"Unsupported SourceType: {SourceType}");
            }
        }
        catch (Exception ex)
        {
            HandleActivationError($"Error initializing log source for tab '{Header}': {ex.Message}");
            return;
        }

        ReactiveFilteredLogStream?.Dispose();
        _streamSubscriptions.Clear(); // Clear previous subscriptions
        ReactiveFilteredLogStream = new ReactiveFilteredLogStream(LogSource, LogDoc, _uiContext, AddLineToLogDocumentCallback, _backgroundScheduler);
        SubscribeToFilteredStream();
        ReactiveFilteredLogStream.Reset();

        if (SourceType == SourceType.File && !string.IsNullOrEmpty(SourceIdentifier))
        {
            ResetLogDocumentAndCollections();
            TotalLogLines = 0; // Reset before PrepareAndGetInitialLinesAsync updates it
            try
            {
                long initialLines = await LogSource.PrepareAndGetInitialLinesAsync(SourceIdentifier, AddLineToLogDocumentCallback).ConfigureAwait(true); //ConfigureAwait(true) for UI context affinity if needed by callback, though callback is simple.
                _uiContext.Post(_ => TotalLogLines = initialLines, null);
                LogSource.StartMonitoring();
            }
            catch (Exception ex)
            {
                HandleActivationError($"Error preparing file source for tab '{Header}': {ex.Message}");
                return;
            }
        }
        else if (SourceType == SourceType.Simulator && !string.IsNullOrEmpty(SourceIdentifier))
        {
            ResetLogDocumentAndCollections();
            TotalLogLines = 0; // Reset before PrepareAndGetInitialLinesAsync updates it
            // Simulator's PrepareAndGetInitialLinesAsync typically returns 0, but TotalLogLines will update via stream.
            await LogSource.PrepareAndGetInitialLinesAsync(SourceIdentifier, AddLineToLogDocumentCallback).ConfigureAwait(false);
            LogSource.StartMonitoring(); // This will start the ISimulatorLogSource
        }
        else if (SourceType == SourceType.Pasted)
        {
            // LogDoc is assumed to be populated by LoadPastedContent prior to ActivateAsync.
            // Or, it might be an empty pasted tab being activated for the first time.
            // ResetLogDocumentAndCollections() is NOT called here for Pasted type
            // as LoadPastedContent already handles it. If it's an empty pasted tab, LogDoc is already empty.
            TotalLogLines = LogDoc.Count;
        }

        ApplyFiltersFromProfile(availableProfiles, globalContextLines);

        IsAutoScrollEnabled = globalAutoScrollEnabled; // Apply global setting

        // Remove LoadingToken only after all setup, including initial filtering, is done or queued.
        // The ReplaceFilteredUpdate with IsInitialLoadProcessingComplete will handle removing LoadingToken.
        // However, if ActivateAsync itself completes quickly without a big initial load,
        // we might need to remove the token here if the stream doesn't immediately signal completion of initial load.
        // Let's assume ReplaceFilteredUpdate handles it. If not, add:
        // _uiContext.Post(_ => CurrentBusyStates.Remove(LoadingToken), null);
        Debug.WriteLine($"---> TabViewModel '{Header}': Activation initiated. Stream will handle filter processing.");
    }

    private void HandleActivationError(string errorMessage)
    {
        Debug.WriteLine($"!!! TabViewModel '{Header}': Activation Error - {errorMessage}");
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Remove(LoadingToken); // Ensure token is removed on error
            CurrentBusyStates.Remove(FilteringToken); // And filtering token
            MessageBox.Show(errorMessage, "Tab Activation Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }, null);
        IsActive = false; // Ensure IsActive is false on error
        UpdateDisplayHeader();
        // Consider further cleanup, e.g., Deactivate() without re-entry checks
        LogSource?.StopMonitoring();
        LogSource?.Dispose();
        LogSource = null;
        ReactiveFilteredLogStream?.Dispose();
        ReactiveFilteredLogStream = null;
    }

    // Deactivate method remains largely the same
    public void Deactivate()
    {
        if (!IsActive) return;
        Debug.WriteLine($"---> TabViewModel '{Header}': Deactivating.");
        IsActive = false; // Set IsActive before stopping, so IsLoading reflects correctly

        // Stop log source first to prevent further updates
        LogSource?.StopMonitoring();

        // Dispose stream and its subscriptions
        ReactiveFilteredLogStream?.Dispose();
        ReactiveFilteredLogStream = null;
        _streamSubscriptions.Clear();

        // Dispose log source
        LogSource?.Dispose();
        LogSource = null;

        LastActivityTimestamp = DateTime.Now;
        UpdateDisplayHeader();

        // Clear busy states related to this tab when deactivating
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Remove(LoadingToken);
            CurrentBusyStates.Remove(FilteringToken);
        }, null);

        Debug.WriteLine($"---> TabViewModel '{Header}': Deactivation complete.");
    }


    private void AddLineToLogDocumentCallback(string line) => LogDoc.AppendLine(line);

    private void SubscribeToFilteredStream()
    {
        _streamSubscriptions.Clear();
        if (ReactiveFilteredLogStream == null) return;

        _streamSubscriptions.Add(ReactiveFilteredLogStream.FilteredUpdates
            .ObserveOn(_uiContext)
            .Subscribe(update => ApplyFilteredUpdateToThisTab(update),
                       ex => HandleProcessorError("Log Processing Error in Tab", ex)));

        _streamSubscriptions.Add(ReactiveFilteredLogStream.TotalLinesProcessed
            .Sample(TimeSpan.FromMilliseconds(200), _backgroundScheduler ?? Scheduler.Default)
            .ObserveOn(_uiContext)
            .Subscribe(count => TotalLogLines = count,
                       ex => HandleProcessorError("Total Lines Error in Tab", ex)));
    }

    private void ApplyFilteredUpdateToThisTab(FilteredUpdateBase update)
    {
        bool wasInitialLoad = CurrentBusyStates.Contains(LoadingToken);
        _uiContext.Post(_ => { if (!CurrentBusyStates.Contains(FilteringToken)) CurrentBusyStates.Add(FilteringToken); }, null);
        bool newLinesWereAppended = false;

        if (update is AppendFilteredUpdate appendUpdate)
        {
            var linesActuallyAdded = new List<FilteredLogLine>();
            foreach (var lineToAdd in appendUpdate.Lines)
            {
                if (_existingOriginalLineNumbers.Add(lineToAdd.OriginalLineNumber))
                {
                    FilteredLogLines.Add(lineToAdd);
                    linesActuallyAdded.Add(lineToAdd);
                    newLinesWereAppended = true;
                }
            }
            if (newLinesWereAppended)
            {
                ScheduleLogTextAppend(linesActuallyAdded);
                // if (IsAutoScrollEnabled) RequestScrollToEnd?.Invoke(this, EventArgs.Empty); // TODO: AutoScroll per tab
            }
        }
        else if (update is ReplaceFilteredUpdate replaceUpdate)
        {
            int originalLineToRestore = HighlightedOriginalLineNumber;
            _existingOriginalLineNumbers.Clear();
            FilteredLogLines.Clear();
            foreach (var line in replaceUpdate.Lines)
            {
                FilteredLogLines.Add(line);
                _existingOriginalLineNumbers.Add(line.OriginalLineNumber);
            }
            ScheduleLogTextUpdate(FilteredLogLines);

            if (originalLineToRestore > 0)
            {
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;
                _uiContext.Post(idx => { HighlightedFilteredLineIndex = (int)idx!; }, newIndex);
                if (newIndex != -1) RequestScrollToLineIndex?.Invoke(this, newIndex);
            }
            else
            {
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            if (replaceUpdate.IsInitialLoadProcessingComplete)
            {
                Debug.WriteLineIf(wasInitialLoad, $"{DateTime.Now:HH:mm:ss.fff} TabVM ApplyFilteredUpdate: Initial Load Processing Complete for tab '{Header}'.");
                _uiContext.Post(_ => { if (wasInitialLoad) CurrentBusyStates.Remove(LoadingToken); }, null);
            }
        }

        if (newLinesWereAppended && IsAutoScrollEnabled)
        {
            RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine($"---> TabViewModel '{Header}': Raised RequestScrollToEnd (AutoScroll Enabled).");
        }
        else if (newLinesWereAppended) // Still log if lines were appended but scroll not requested
        {
            Debug.WriteLine($"---> TabViewModel '{Header}': New lines appended, RequestScrollToEnd NOT raised (AutoScroll Disabled: {IsAutoScrollEnabled}).");
        }
        _uiContext.Post(_ => CurrentBusyStates.Remove(FilteringToken), null); // Now remove token
    }

    public int FilteredLogLinesCount => FilteredLogLines.Count;

    private void HandleProcessorError(string contextMessage, Exception ex)
    {
        Debug.WriteLine($"!!! TabViewModel '{Header}' - {contextMessage}: {ex}");
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Clear(); // Clear all busy states for this tab on error
            MessageBox.Show($"Error in tab '{Header}': {ex.Message}", "Tab Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }, null);
    }

    public void LoadPastedContent(string text)
    {
        if (SourceType != SourceType.Pasted)
            throw new InvalidOperationException("LoadPastedContent can only be called on PastedSourceType tabs.");
        ResetLogDocumentAndCollections();
        LogDoc.AddInitialLines(text);
        TotalLogLines = LogDoc.Count;
        IsModified = true;
    }
    
    private void ResetLogDocumentAndCollections()
    {
        LogDoc.Clear();
        _existingOriginalLineNumbers.Clear();
        FilteredLogLines.Clear();
        ScheduleLogTextUpdate(FilteredLogLines); // Clear editor content
        TotalLogLines = 0;
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
        if (IsActive && FilteredLogLines.Any()) ScheduleLogTextUpdate(FilteredLogLines);
    }

    private void ScheduleLogTextAppend(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        var linesSnapshot = linesToAppend.ToList();
        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!;
            if (_logEditorInstance?.Document != null) AppendLogTextInternal(lines);
            UpdateSearchMatches();
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
        var linesSnapshot = relevantLines.ToList();
        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!;
            if (_logEditorInstance?.Document != null) ReplaceLogTextInternal(lines);
            UpdateSearchMatches();
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

    // Called when this tab's AssociatedFilterProfileName changes or when global context lines change
    public void ApplyFiltersFromProfile(IEnumerable<FilterProfileViewModel> availableProfiles, int globalContextLines)
    {
        if (!IsActive || ReactiveFilteredLogStream == null) return;

        var profileVM = availableProfiles.FirstOrDefault(p => p.Name == AssociatedFilterProfileName);
        IFilter? filterToApply = profileVM?.Model?.RootFilter ?? new TrueFilter();

        Debug.WriteLine($"---> TabViewModel '{Header}': Applying filters. Profile: '{AssociatedFilterProfileName}', Filter: '{filterToApply.GetType().Name}', Context: {globalContextLines}");
        _uiContext.Post(_ => { if (!CurrentBusyStates.Contains(FilteringToken)) CurrentBusyStates.Add(FilteringToken); }, null);
        ReactiveFilteredLogStream.UpdateFilterSettings(filterToApply, globalContextLines);
        UpdateFilterHighlightModels(filterToApply); // For AvalonEdit highlighting
    }

    private void UpdateFilterHighlightModels(IFilter? rootFilter)
    {
        var newFilterModels = new ObservableCollection<IFilter>();
        if (rootFilter != null) TraverseFilterTreeForHighlightingRecursive(rootFilter, newFilterModels);
        FilterHighlightModels = newFilterModels;
    }

    private void TraverseFilterTreeForHighlightingRecursive(IFilter filter, ObservableCollection<IFilter> models)
    {
        if (!filter.Enabled) return;
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
        if (_currentSearchIndex == -1) _currentSearchIndex = _searchMatches.Count - 1;
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
            HighlightedFilteredLineIndex = lineIndex;
            if(lineIndex != -1) RequestScrollToLineIndex?.Invoke(this, lineIndex);
        }
        else
        {
            CurrentMatchOffset = -1; CurrentMatchLength = 0; HighlightedFilteredLineIndex = -1;
        }
        OnPropertyChanged(nameof(SearchStatusText));
    }

    partial void OnSearchTextChanged(string value) => UpdateSearchMatches();
    partial void OnIsCaseSensitiveSearchChanged(bool value) => UpdateSearchMatches();

    private string GetCurrentDocumentTextForSearch()
    {
        if (_logEditorInstance?.Document != null) return _logEditorInstance.Document.Text;
        return FilteredLogLines.Any() ? string.Join(Environment.NewLine, FilteredLogLines.Select(fll => fll.Text)) : string.Empty;
    }

    private void UpdateSearchMatches()
    {
        string currentSearchTerm = SearchText;
        string textToSearch = GetCurrentDocumentTextForSearch();
        ResetSearchState(); // Clears _searchMatches, SearchMarkers, _currentSearchIndex

        if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
        {
            OnPropertyChanged(nameof(SearchStatusText));
            return;
        }

        int offset = 0;
        var stringComparison = IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var tempMarkers = new List<SearchResult>();
        while (offset < textToSearch.Length)
        {
            int foundIndex = textToSearch.IndexOf(currentSearchTerm, offset, stringComparison);
            if (foundIndex == -1) break;
            var newMatch = new SearchResult(foundIndex, currentSearchTerm.Length);
            _searchMatches.Add(newMatch);
            tempMarkers.Add(newMatch);
            offset = foundIndex + currentSearchTerm.Length;
        }
        foreach (var marker in tempMarkers) SearchMarkers.Add(marker); // Update observable collection once
        
        if (_searchMatches.Any()) _currentSearchIndex = 0; // Select first match if any
        SelectAndScrollToCurrentMatch(); // This also updates SearchStatusText via OnPropertyChanged
    }
    
    private void ResetSearchState()
    {
        _searchMatches.Clear();
        SearchMarkers.Clear(); // This is an ObservableCollection, Clear() will notify
        _currentSearchIndex = -1;
        // CurrentMatchOffset and CurrentMatchLength will be reset by SelectAndScrollToCurrentMatch
    }

    private int FindFilteredLineIndexContainingOffset(int charOffset)
    {
        int currentCumulativeOffset = 0;
        for (int i = 0; i < FilteredLogLines.Count; i++)
        {
            var line = FilteredLogLines[i];
            int lineEndOffset = currentCumulativeOffset + line.Text.Length;
            // Check if charOffset is within the current line (inclusive of start, exclusive of end of line content for zero-length matches)
            // or if it's the start of a zero-length match at the very end of the line.
            if (charOffset >= currentCumulativeOffset && charOffset <= lineEndOffset)
            {
                return i;
            }
            currentCumulativeOffset = lineEndOffset + Environment.NewLine.Length;
        }
        return -1;
    }

    private string GenerateSearchStatusText()
    {
        if (string.IsNullOrEmpty(SearchText) || !FilteredLogLines.Any()) return "";
        if (_searchMatches.Count == 0) return "Phrase not found";
        if (_currentSearchIndex == -1 && _searchMatches.Any()) return $"{_searchMatches.Count} matches found"; // Before first selection
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
            HighlightedFilteredLineIndex = foundLine.Index;
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
        await Task.Delay(2500); // This real delay is fine for the app. Tests will await the command.
        if (IsJumpTargetInvalid) // Check if still relevant to clear
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
            HighlightedOriginalLineNumber = -1;
            Debug.WriteLine($"TabVM: HighlightedFilteredLineIndex changed to {newValue} (invalid), Original set to -1");
        }
    }

    partial void OnHighlightedOriginalLineNumberChanged(int oldValue, int newValue)
    {
        // This updates TargetOriginalLineNumberInput, which is for the "Go To" box.
        // It does NOT try to find the filtered index here, as that could lead to loops
        // if the original line number isn't currently in the filtered view.
        // The primary update path is:
        // UI Click/Caret -> MainWindow sets MainViewModel.HighlightedOriginalLineNumber ->
        // TabViewModel.HighlightedOriginalLineNumber gets set -> TabViewModel.OnHighlightedOriginalLineNumberChanged updates its TargetOriginalLineNumberInput.
        // The SelectedIndexHighlightTransformer uses MainViewModel.HighlightedOriginalLineNumber to draw.
        if (!_isEditingTargetOriginalLineNumber) 
        {
            TargetOriginalLineNumberInput = (newValue > 0) ? newValue.ToString() : string.Empty;
        }
        Debug.WriteLine($"TabVM: HighlightedOriginalLineNumber changed to {newValue}. Input box updated: {TargetOriginalLineNumberInput}");
    }

    // Helper to prevent feedback loop with TargetOriginalLineNumberInput
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
            Deactivate();
            _streamSubscriptions.Dispose();
            CurrentBusyStates.Clear(); // Clear busy states on dispose
        }
        _disposed = true;
    }
}

// Simple NullLogSource for pasted content or other non-streaming scenarios
public class NullLogSource : ILogSource
{
    public IObservable<string> LogLines => Observable.Empty<string>();
    public Task<long> PrepareAndGetInitialLinesAsync(string sourceIdentifier, Action<string> addLineToDocumentCallback) => Task.FromResult(0L);
    public void StartMonitoring() { }
    public void StopMonitoring() { }
    public void Dispose() { }
}
