// ===== File: src\Logonaut.UI\MainViewModel.cs =====

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Windows; // For Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Services;
using Logonaut.LogTailing;
using ICSharpCode.AvalonEdit;

namespace Logonaut.UI.ViewModels;

// NOTICE: Signals MainWindow code-behind (via RequestTextUpdate event) for direct
// TextEditor.Document updates instead of pure data binding.
// Justification: Performance optimization for large log data; direct document
// manipulation is significantly faster than string binding for AvalonEdit.
// Also holds state tightly coupled to View for features like search navigation.
public partial class MainViewModel : ObservableObject, IDisposable
{
    #region // --- Fields ---

    // --- UI Services & Context ---
    public static readonly object LoadingToken = new();
    public static readonly object FilteringToken = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private ILogSource _logSource; // Instance is managed here now
    private readonly SynchronizationContext _uiContext;
    private readonly ILogFilterProcessor _logFilterProcessor;

    // --- Lifecycle Management ---
    private readonly CompositeDisposable _disposables = new();

    // --- Search State & Ruler Markers ---
    private List<SearchResult> _searchMatches = new();
    private int _currentSearchIndex = -1;

    // Observable property bound to OverviewRulerMargin.SearchMarkers
    [ObservableProperty] private ObservableCollection<SearchResult> _searchMarkers = new();

    [ObservableProperty] private bool _isAutoScrollEnabled = true;

    public event EventHandler? RequestScrollToEnd; // Triggered when Auto Scroll is enabled

    #endregion // --- Fields ---

    #region // --- Stats Properties ---

    [ObservableProperty]
    private long _totalLogLines; // Bound to TotalLinesProcessed

    // No need for ObservableProperty here, just needs to raise notification
    public int FilteredLogLinesCount => FilteredLogLines.Count;

    #endregion // --- Stats Properties ---

    #region // --- Constructor ---

    public MainViewModel(
        ISettingsService settingsService,
        IFileDialogService? fileDialogService = null,
        ILogFilterProcessor? logFilterProcessor = null, // Optional for testing
        ILogSource? initialLogSource = null,          // Optional for testing
        SynchronizationContext? uiContext = null)
    {
        _settingsService = settingsService;
        _fileDialogService = fileDialogService ?? new FileDialogService();
        _uiContext = uiContext ?? SynchronizationContext.Current ??
                        throw new InvalidOperationException("Could not capture or receive a valid SynchronizationContext.");

        bool useSimulator = true; // Hard coded override for testing
        if (initialLogSource != null)
            _logSource = initialLogSource; // Allow injection for tests to override
        else if (useSimulator) {
            _logSource = new SimulatorLogSource(linesPerSecond: 4); // Example: 50 lines/sec
            _logSource.StartMonitoring();
        } else
            _logSource = new FileLogSource(); // Default to file source
        _disposables.Add(_logSource); // Add source to disposables for cleanup

        _logFilterProcessor = logFilterProcessor ?? new LogFilterProcessor(
            _logSource, // Pass the ILogSource instance
            LogDoc,
            _uiContext,
            AddLineToLogDocument);

        CurrentBusyStates.CollectionChanged += CurrentBusyStates_CollectionChanged;
        _disposables.Add(Disposable.Create(() => {
            CurrentBusyStates.CollectionChanged -= CurrentBusyStates_CollectionChanged;
        }));

        var filterSubscription = _logFilterProcessor.FilteredUpdates
            .Subscribe(
                update => ApplyFilteredUpdate(update),
                ex => HandleProcessorError("Log Processing Error", ex)
            );

        var samplingScheduler = Scheduler.Default;
        _totalLinesSubscription = _logFilterProcessor.TotalLinesProcessed
            .Sample(TimeSpan.FromMilliseconds(200), samplingScheduler)
            .ObserveOn(_uiContext)
            .Subscribe(
                count => ProcessTotalLinesUpdate(count),
                ex => HandleProcessorError("Total Lines Error", ex)
            );

        _disposables.Add(_logFilterProcessor);
        _disposables.Add(filterSubscription);
        _disposables.Add(_totalLinesSubscription);

        Theme = new ThemeViewModel();
        LoadPersistedSettings();
    }

    private void CurrentBusyStates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Whenever the collection changes (add, remove, reset),
        // notify the UI that the IsLoading property *might* have changed.
        // The binding system will then re-query the IsLoading getter.
        OnPropertyChanged(nameof(IsLoading));
    }

    #endregion // --- Constructor ---

    #region // --- Highlighted Line State ---

    // The 0-based index of the highlighted line within the FilteredLogLines collection.
    // Bound to the PersistentLineHighlightRenderer.HighlightedLineIndex DP.
    [ObservableProperty] private int _highlightedFilteredLineIndex = -1;

    // The original line number (1-based) corresponding to the highlighted line.
    // Used to restore selection after re-filtering.
    [ObservableProperty] private int _highlightedOriginalLineNumber = -1;

    // Update original line number whenever the filtered index changes
    partial void OnHighlightedFilteredLineIndexChanged(int value)
    {
        if (value >= 0 && value < FilteredLogLines.Count)
        {
            HighlightedOriginalLineNumber = FilteredLogLines[value].OriginalLineNumber;
        }
        else
        {
            HighlightedOriginalLineNumber = -1; // No valid line highlighted
        }
    }

    // Update original line number whenever the filtered index changes
    // ALSO: Update the Target Input box when a line is clicked (if not focused)
    partial void OnHighlightedOriginalLineNumberChanged(int value)
    {
        // Only update the input box if the user isn't actively editing it.
        // We need a way to know if the TextBox has focus. We can approximate
        // by checking if the new value matches the input, but a dedicated
        // IsFocused property (updated via triggers in XAML) would be more robust.
        // For simplicity now, let's just update it. The user might lose input,
        // which is a minor inconvenience.
        TargetOriginalLineNumberInput = (value > 0) ? value.ToString() : string.Empty;
    }

    #endregion // --- Highlighted Line State ---

    #region Jump To Line Command

    [RelayCommand(CanExecute = nameof(CanJumpToLine))]
    private async Task JumpToLine() // Make async for delay
    {
        IsJumpTargetInvalid = false; // Reset feedback
        JumpStatusMessage = string.Empty;

        if (!int.TryParse(TargetOriginalLineNumberInput, out int targetLineNumber) || targetLineNumber <= 0)
        {
            JumpStatusMessage = "Invalid line number.";
            await TriggerInvalidInputFeedback();
            return;
        }

        // Find the line in the *current* filtered list
        var foundLine = FilteredLogLines
            .Select((line, index) => new { line.OriginalLineNumber, Index = index })
            .FirstOrDefault(item => item.OriginalLineNumber == targetLineNumber);

        if (foundLine != null)
        {
            // Found the line in the filtered view!
            // Set the highlighted index. This will trigger PropertyChanged.
            // The MainWindow code-behind will observe this change and trigger the scroll.
            HighlightedFilteredLineIndex = foundLine.Index;
        }
        else
        {
            // Line exists in original log but not in filtered view
            // OR line number is beyond the original log's total lines
            // (We can't easily distinguish without checking LogDoc count, which might be large)
            JumpStatusMessage = $"Line {targetLineNumber} not found in filtered view.";
            await TriggerInvalidInputFeedback();
        }
    }

    private bool CanJumpToLine() => !string.IsNullOrWhiteSpace(TargetOriginalLineNumberInput);

    private async Task TriggerInvalidInputFeedback()
    {
        IsJumpTargetInvalid = true;
        await Task.Delay(2500); // Use Task.Delay
        if (IsJumpTargetInvalid)
        {
            IsJumpTargetInvalid = false;
            JumpStatusMessage = string.Empty;
        }
    }

    #endregion // Jump To Line Command

    #region // --- UI State Management ---

    private void ProcessTotalLinesUpdate(long count)
    {
            TotalLogLines = count;
    }

    partial void OnIsAutoScrollEnabledChanged(bool value)
    {
        if (value == true && HighlightedFilteredLineIndex != -1)
            HighlightedFilteredLineIndex = -1;

        if (value == true)
            RequestScrollToEnd?.Invoke(this, EventArgs.Empty);

        SaveCurrentSettingsDelayed();
    }

    public ThemeViewModel Theme { get; }

    // Central store for all original log lines, passed to processor but owned here.
    // The content is managed by current class.
    public LogDocument LogDoc { get; } = new();

    // Collection of filtered lines currently displayed in the UI.
    public ObservableCollection<FilteredLogLine> FilteredLogLines { get; } = new();

    // While not pure MVVM, the simplest way for this specific performance optimization is to allow the ViewModel to interact directly (but safely via the dispatcher)
    // with the TextEditor's document.
    private TextEditor? _logEditorInstance;
    public void SetLogEditorInstance(TextEditor editor)
    {
        _logEditorInstance = editor;
        if (FilteredLogLines.Any())
        {
            ScheduleLogTextUpdate(FilteredLogLines);
        }
    }

    // Text entered by the user for searching.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextSearchCommand))]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private string _searchText = "";

    // Properties for target selection in AvalonEdit
    [ObservableProperty] private int _currentMatchOffset = -1;
    [ObservableProperty] private int _currentMatchLength = 0;

    // Status text for search
    public string SearchStatusText
    {
        get
        {
            if (string.IsNullOrEmpty(SearchText)) return "";
            if (_searchMatches.Count == 0) return "Phrase not found";
            if (_currentSearchIndex == -1) return $"{_searchMatches.Count} matches found";
            return $"Match {_currentSearchIndex + 1} of {_searchMatches.Count}";
        }
    }

    // Path of the currently monitored log file.
    [ObservableProperty]
    private string? _currentLogFilePath;

    // Configured number of context lines to display around filter matches.
    [ObservableProperty]
    private int _contextLines = 0;

    // Controls visibility of the custom line number margin.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomLineNumberMarginVisible))]
    private bool _showLineNumbers = true;
    public Visibility IsCustomLineNumberMarginVisible => ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

    partial void OnShowLineNumbersChanged(bool value) => SaveCurrentSettingsDelayed();

    // Controls whether timestamp highlighting rules are applied in AvalonEdit.
    [ObservableProperty] private bool _highlightTimestamps = true;
    partial void OnHighlightTimestampsChanged(bool value) => SaveCurrentSettingsDelayed();

    // Collection of filter patterns (substrings/regex) for highlighting.
    // Note: This state is derived by traversing the *active* FilterProfile.
    [ObservableProperty] private ObservableCollection<string> _filterSubstrings = new();

    // Observable collection holding tokens representing active background tasks.
    // Bound to the UI's BusyIndicator; the indicator spins if this collection is not empty.
    // Add/remove specific tokens (e.g., LoadingToken) to control the busy state.
    public ObservableCollection<object> CurrentBusyStates { get; } = new();
    public bool IsLoading => CurrentBusyStates.Contains(LoadingToken);

    private IDisposable? _totalLinesSubscription;

    // Controls whether search is case sensitive
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private bool _isCaseSensitiveSearch = false;

    // The filter node currently selected within the TreeView of the ActiveFilterProfile.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))] // Enable adding if composite selected
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))] // Enable removing selected node
    [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))] // Enable editing selected node
    private FilterViewModel? _selectedFilterNode;

    // Collection exposed specifically for the TreeView's ItemsSource.
    // Contains only the root node of the ActiveFilterProfile's tree.
    public ObservableCollection<FilterViewModel> ActiveTreeRootNodes { get; } = new();

    private void LoadPersistedSettings()
    {
        LogonautSettings settings;
        settings = _settingsService.LoadSettings();
        LoadFilterProfiles(settings);
        ShowLineNumbers = settings.ShowLineNumbers;
        HighlightTimestamps = settings.HighlightTimestamps;
        IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;
        ContextLines = settings.ContextLines;
        IsAutoScrollEnabled = settings.AutoScrollToTail;
    }

    private void SaveCurrentSettingsDelayed() => _uiContext.Post(_ => SaveCurrentSettings(), null);

    private void SaveCurrentSettings()
    {
        var settingsToSave = new LogonautSettings
        {
            ContextLines = this.ContextLines,
            ShowLineNumbers = this.ShowLineNumbers,
            HighlightTimestamps = this.HighlightTimestamps,
            IsCaseSensitiveSearch = this.IsCaseSensitiveSearch,
            AutoScrollToTail = this.IsAutoScrollEnabled,
        };
        SaveFilterProfiles(settingsToSave);
        _settingsService.SaveSettings(settingsToSave);
    }
    #endregion // --- UI State Management ---

    #region // --- Command Handling ---

    // === Filter Node Manipulation Commands (Operate on Active Profile's Tree) ===

        // Combined Add Filter command - type determined by parameter
    [RelayCommand(CanExecute = nameof(CanAddFilterNode))]
    private void AddFilter(object? filterTypeParam) // Parameter likely string like "Substring", "And", etc.
    {
        if (ActiveFilterProfile == null) throw new InvalidOperationException("No active profile");

        IFilter newFilterNodeModel;
        string type = filterTypeParam as string ?? string.Empty;
        switch(type)
        {
            case "Substring": newFilterNodeModel = new SubstringFilter(""); break;
            case "Regex": newFilterNodeModel = new RegexFilter(".*"); break;
            case "And": newFilterNodeModel = new AndFilter(); break;
            case "Or": newFilterNodeModel = new OrFilter(); break;
            case "Nor": newFilterNodeModel = new NorFilter(); break;
            default: return; // Unknown type
        }

            // Note: The callback passed down when creating FilterViewModel ensures TriggerFilterUpdate is called
        FilterViewModel newFilterNodeVM = new FilterViewModel(newFilterNodeModel, TriggerFilterUpdate);
        FilterViewModel? targetParentVM = SelectedFilterNode ?? ActiveFilterProfile.RootFilterViewModel;

            // Case 1: Active profile's tree is currently empty
        if (ActiveFilterProfile.RootFilterViewModel == null)
        {
                ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel); // This sets model and refreshes RootFilterViewModel
                UpdateActiveTreeRootNodes(ActiveFilterProfile); // Explicitly update the collection bound to the TreeView
                SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel; // Select the new root
        }
            // Case 2: A composite node is selected - add as child
        else if (SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter)
        {
                // AddChildFilter takes the MODEL, adds it, creates the child VM, and adds to Children collection
            SelectedFilterNode.AddChildFilter(newFilterNodeModel);
                SelectedFilterNode.IsExpanded = true; // Expand parent

                // Find and select the newly created child VM
            var addedVM = SelectedFilterNode.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);
            if (addedVM != null) SelectedFilterNode = addedVM;
        }
            // Case 3: No node selected (but tree exists), or non-composite selected - Replace root? Show Error?
            // Current Requirement: Select a composite node first. Let's enforce this.
            else // No node selected or non-composite selected
        {
            throw new InvalidOperationException("Unexpected state: No node selected or non-composite selected. This should not happen with current logic.");
        }

        // Editing logic: If the new node is editable, start editing
        if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
        {
           SelectedFilterNode.BeginEditCommand.Execute(null);
        }
        SaveCurrentSettingsDelayed();
    }
    private bool CanAddFilterNode()
    {
        if (ActiveFilterProfile == null) return false;
        bool isTreeEmpty = ActiveFilterProfile.RootFilterViewModel == null;
        bool isCompositeNodeSelected = SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter;
        return isTreeEmpty || isCompositeNodeSelected;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFilterNode))]
    private void RemoveFilterNode()
    {
        if (SelectedFilterNode == null || ActiveFilterProfile?.RootFilterViewModel == null) return;
        FilterViewModel? parent = SelectedFilterNode.Parent;

        // Case 1: Removing the root node of the active profile
        if (SelectedFilterNode == ActiveFilterProfile.RootFilterViewModel)
        {
            ActiveFilterProfile.SetModelRootFilter(null);
            UpdateActiveTreeRootNodes(ActiveFilterProfile);
            SelectedFilterNode = null;
        }
        // Case 2: Removing a child node
        else if (parent != null)
        {
            var nodeToRemove = SelectedFilterNode;
            SelectedFilterNode = parent;
            parent.RemoveChild(nodeToRemove);
        }
        SaveCurrentSettingsDelayed();
    }
    private bool CanRemoveFilterNode() => SelectedFilterNode != null && ActiveFilterProfile != null;

    [RelayCommand(CanExecute = nameof(CanToggleEditNode))]
    private void ToggleEditNode()
    {
        if (SelectedFilterNode?.IsEditable ?? false)
        {
            if (SelectedFilterNode.IsNotEditing) SelectedFilterNode.BeginEditCommand.Execute(null);
            else SelectedFilterNode.EndEditCommand.Execute(null);
            SaveCurrentSettingsDelayed(); // Save potentially after EndEdit completes
        }
    }
    private bool CanToggleEditNode() => SelectedFilterNode?.IsEditable ?? false;

    [RelayCommand] private async Task OpenLogFileAsync()
    {
        if (_logSource is SimulatorLogSource)
        {
            _logSource.StopMonitoring();
            _logSource.Dispose(); // Dispose the old simulator
            _logSource = new FileLogSource(); // Create the file source
            // NOTE: The LogFilterProcessor holds a reference to the *old* source.
            // Ideally, we'd re-create the processor or inject the new source into it.
            // For minimal change now, we accept this limitation - reopening a file
            // after using the simulator might require restarting the app if the processor isn't updated.
            // A better approach involves an ILogSourceProvider or allowing source injection into the processor post-construction.
            // For now, we'll proceed, but acknowledge this potential issue.
            // Re-add the new source to disposables (though this might lead to double disposal if VM is disposed later)
             // _disposables.Add(_logSource); // This is risky, manage source lifecycle carefully.
        }
        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
        if (string.IsNullOrEmpty(selectedFile)) return;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: '{selectedFile}'");

        _uiContext.Post(_ => CurrentBusyStates.Add(LoadingToken), null);

        try
        {
            // 1. Stop previous source monitoring (if any)
             _logSource.StopMonitoring();

            // 2. Reset Processor State
            _logFilterProcessor.Reset();

            // 3. Clear ViewModel/UI State
            FilteredLogLines.Clear();
            OnPropertyChanged(nameof(FilteredLogLinesCount));
            ScheduleLogTextUpdate(FilteredLogLines); // Clear editor content
            CurrentLogFilePath = selectedFile;

            // 4. Prepare Log Source and Read Initial Lines (populates LogDoc)
            LogDoc.Clear(); // Clear document before reading
            long initialLines = await _logSource.PrepareAndGetInitialLinesAsync(selectedFile, AddLineToLogDocument).ConfigureAwait(true);

            // 5. Update Total Lines Display immediately
            _uiContext.Post(_ => TotalLogLines = initialLines, null);

            // 6. Start Monitoring *New* Lines from Source
             _logSource.StartMonitoring();

            // 7. Trigger the First Filter Explicitly
            _uiContext.Post(_ => CurrentBusyStates.Add(FilteringToken), null);
            IFilter? firstFilter = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
            _logFilterProcessor.UpdateFilterSettings(firstFilter, ContextLines);
            UpdateFilterSubstrings();

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Prepare/Start completed ({initialLines} lines). First filter triggered.");
            // LoadingToken removed by ApplyFilteredUpdate handling the *first* Replace.
            // FilteringToken removed by ApplyFilteredUpdate.
        }
        catch (Exception ex)
        {
            _uiContext.Post(_ => {
                CurrentBusyStates.Remove(LoadingToken);
                CurrentBusyStates.Remove(FilteringToken);
                MessageBox.Show($"Error opening or reading log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentLogFilePath = null;
                FilteredLogLines.Clear();
                OnPropertyChanged(nameof(FilteredLogLinesCount));
                TotalLogLines = 0;
                 _logSource?.StopMonitoring();
            }, null);
        }
    }


        [RelayCommand(CanExecute = nameof(CanSearch))] private void PreviousSearch()
    {
        if (_searchMatches.Count == 0) return;
        if (_currentSearchIndex == -1) _currentSearchIndex = _searchMatches.Count - 1;
        else _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        SelectAndScrollToCurrentMatch();
        OnPropertyChanged(nameof(SearchStatusText));
    }

    [RelayCommand(CanExecute = nameof(CanSearch))] private void NextSearch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatches.Count; // Wrap around
        SelectAndScrollToCurrentMatch();
        OnPropertyChanged(nameof(SearchStatusText));
    }
    private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

    private void SelectAndScrollToCurrentMatch()
    {
        if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchMatches.Count)
        {
            var match = _searchMatches[_currentSearchIndex];
            CurrentMatchOffset = match.Offset;
            CurrentMatchLength = match.Length;
            int lineIndex = FindFilteredLineIndexContainingOffset(CurrentMatchOffset);
            HighlightedFilteredLineIndex = lineIndex;
        }
        else
        {
            CurrentMatchOffset = -1;
            CurrentMatchLength = 0;
            HighlightedFilteredLineIndex = -1;
        }
    }
    
    // Helper method to find the index in FilteredLogLines containing a character offset
    // Note: This is inefficient for large logs. See comments in SelectAndScrollToCurrentMatch.
    private int FindFilteredLineIndexContainingOffset(int offset)
    {
        int currentOffset = 0;
        for (int i = 0; i < FilteredLogLines.Count; i++)
        {
            int lineLength = FilteredLogLines[i].Text.Length;
            int lineEndOffset = currentOffset + lineLength;
            if (offset >= currentOffset && offset <= lineEndOffset) return i;
            currentOffset = lineEndOffset + Environment.NewLine.Length;
        }
        return -1;
    }
    partial void OnSearchTextChanged(string value) => UpdateSearchMatches();

    private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
    {
        var newFilterSubstrings = new ObservableCollection<string>();
        // Traverse the tree of the *currently active* profile
        if (ActiveFilterProfile?.RootFilterViewModel != null)
            TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterSubstrings);
        FilterSubstrings = newFilterSubstrings; // Update the property bound to AvalonEditHelper
    }

    [RelayCommand(CanExecute = nameof(CanDecrementContextLines))]
    private void DecrementContextLines() => ContextLines = Math.Max(0, ContextLines - 1);
    private bool CanDecrementContextLines() => ContextLines > 0;

    [RelayCommand] private void IncrementContextLines() => ContextLines++;

    partial void OnContextLinesChanged(int value)
    {
        DecrementContextLinesCommand.NotifyCanExecuteChanged();
        TriggerFilterUpdate();
        SaveCurrentSettingsDelayed();
    }

    #endregion // --- Command Handling ---

    // ... (Orchestration & Updates section - most methods remain the same, check dependencies) ...
    #region // --- Orchestration & Updates ---

    private void AddLineToLogDocument(string line) => LogDoc.AppendLine(line);

    private void UpdateActiveTreeRootNodes(FilterProfileViewModel? activeProfile)
    {
        ActiveTreeRootNodes.Clear();
        if (activeProfile?.RootFilterViewModel != null)
            ActiveTreeRootNodes.Add(activeProfile.RootFilterViewModel);
        OnPropertyChanged(nameof(ActiveTreeRootNodes)); // Keep for safety, though collection changes might suffice
    }

    private void TriggerFilterUpdate()
    {
        // Ensure token isn't added multiple times if trigger fires rapidly
        _uiContext.Post(_ => { if (!CurrentBusyStates.Contains(FilteringToken)) CurrentBusyStates.Add(FilteringToken); }, null);
        _uiContext.Post(_ => {
            IFilter? filterToApply = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
            _logFilterProcessor.UpdateFilterSettings(filterToApply, ContextLines);
            UpdateFilterSubstrings();
        }, null);
    }

    private string GetCurrentDocumentText()
    {
        // Priority 1: Use the actual editor document if available (running in UI)
        // Reading Text property is generally thread-safe.
        if (_logEditorInstance?.Document != null) return _logEditorInstance.Document.Text;
        // Priority 2: Fallback for testing or before editor is initialized.
        // Simulate the document content by joining the FilteredLogLines.
        // This allows internal logic like search to work correctly in unit tests.
        if (FilteredLogLines.Any())
        {
            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (var line in FilteredLogLines) { if (!first) sb.Append(Environment.NewLine); sb.Append(line.Text); first = false; }
            return sb.ToString();
        }
        return string.Empty;
    }

    /*
    * Processes updates from the LogFilterProcessor to update the UI state.
    *
    * Responsibilities include:
    *   - Updating the FilteredLogLines collection.
    *   - Scheduling direct TextEditor.Document updates (Append/Replace) on the UI thread.
    *   - Managing busy indicators (IsBusyFiltering, IsPerformingInitialLoad).
    *   - Preserving highlighted line selection during Replace updates.
    *
    * Accepts a FilteredUpdate object containing new lines and the update type (Replace or Append).
    */
    private void ApplyFilteredUpdate(FilteredUpdate update)
    {
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate received. Lines={update.Lines.Count}");
        bool wasInitialLoad = CurrentBusyStates.Contains(LoadingToken);
        bool isAppend = CheckIfAppend(update.Lines);

        if (isAppend)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate: Detected Append.");

            // 1. Calculate the newly added lines
            var appendedLines = update.Lines.Skip(FilteredLogLines.Count).ToList();
            // 2. Add only the new lines to the ObservableCollection
            AddFilteredLines(appendedLines);
            ScheduleLogTextAppend(appendedLines);
            // 4. Trigger Auto-Scroll if enabled
            if (IsAutoScrollEnabled) RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            // 5. Reset BusyFiltering (Append means this batch is done)
             _uiContext.Post(_ => { CurrentBusyStates.Remove(FilteringToken); }, null);
        }
        else // Handle Replace
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate: Detected Replace.");
            int originalLineToRestore = HighlightedOriginalLineNumber;
            // 1. Replace the entire ObservableCollection
            ReplaceFilteredLines(update.Lines);
            // 2. Schedule the full text replace for AvalonEdit
            ScheduleLogTextUpdate(FilteredLogLines);
            // 3. Restore Highlight (only makes sense after a replace)
             if (originalLineToRestore > 0)
            {
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = newIndex; }, null);
            } else {
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            // 4. Replace means filtering is done AND if it was the initial load, loading is also done.
            _uiContext.Post(_ => {
                CurrentBusyStates.Remove(FilteringToken);
                if(wasInitialLoad) CurrentBusyStates.Remove(LoadingToken);
            }, null);
        }
    }

    private bool CheckIfAppend(IReadOnlyList<FilteredLogLine> newLines)
    {
        int currentCount = FilteredLogLines.Count;
        // If the new list isn't longer, it can't be an append.
        if (newLines.Count <= currentCount)
            return false;

        // If the current list is empty, any new lines are technically an append (or initial load)
        // but ApplyFilteredUpdate's 'else' branch handles initial load correctly (Replace).
        // So, only treat it as a UI append if the current list *wasn't* empty.
        if (currentCount == 0)
            return false; // Let the Replace logic handle the initial population.

        // Compare the original line numbers of the existing lines with the start of the new list.
        // This is more robust than comparing text content.
        return newLines.Take(currentCount)
                    .Select(l => l.OriginalLineNumber)
                    .SequenceEqual(FilteredLogLines.Select(l => l.OriginalLineNumber));
    }

    private void ScheduleLogTextAppend(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        // Ensure we work with a copy in case the original list changes
        var linesSnapshot = linesToAppend.ToList();
        _uiContext.Post(state => {
            var lines = (List<FilteredLogLine>)state!;
            AppendLogTextInternal(lines);
            // Update search matches *after* appending text
            UpdateSearchMatches();
        }, linesSnapshot);
    }

    private void AppendLogTextInternal(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        if (_logEditorInstance?.Document == null || !linesToAppend.Any()) return;
        try
        {
            var sb = new System.Text.StringBuilder();

            // Prepend a newline ONLY if the editor has text AND doesn't already end with a newline
            if (_logEditorInstance.Document.TextLength > 0)
            {
                string lastChars = _logEditorInstance.Document.GetText(
                    _logEditorInstance.Document.TextLength - Environment.NewLine.Length,
                    Environment.NewLine.Length);
                if (lastChars != Environment.NewLine)
                {
                    sb.Append(Environment.NewLine);
            }
            }

            // Append the new lines, separated by newlines
            for (int i = 0; i < linesToAppend.Count; i++)
            {
                if (i > 0) // Add newline before second, third, etc. lines
                    sb.Append(Environment.NewLine);
                sb.Append(linesToAppend[i].Text);
            }

            string textToAppend = sb.ToString();
            _logEditorInstance.Document.Insert(_logEditorInstance.Document.TextLength, textToAppend);
        }
        catch (Exception ex) { Debug.WriteLine($"Error appending text to AvalonEdit: {ex.Message}"); }
    }

    // Helper methods called by ApplyFilteredUpdate to modify UI collections.
    private void AddFilteredLines(IReadOnlyList<FilteredLogLine> linesToAdd)
    {
        foreach (var line in linesToAdd) FilteredLogLines.Add(line);
        OnPropertyChanged(nameof(FilteredLogLinesCount));
    }

    private void ReplaceFilteredLines(IReadOnlyList<FilteredLogLine> newLines)
    {
        ResetSearchState();
        FilteredLogLines.Clear();
        foreach (var line in newLines) FilteredLogLines.Add(line);
        OnPropertyChanged(nameof(FilteredLogLinesCount));
    }

    private void ResetSearchState()
    {
        _searchMatches.Clear();
        SearchMarkers.Clear();
        _currentSearchIndex = -1;
        SelectAndScrollToCurrentMatch();
    }

    // Schedules the LogText update to run after current UI operations.
    private void ScheduleLogTextUpdate(IReadOnlyList<FilteredLogLine> relevantLines)
    {
        // Clone relevantLines if it might be modified elsewhere before Post executes?
        // ObservableCollection snapshot for Replace is safe. List passed for Append is safe.
        var linesSnapshot = relevantLines.ToList(); // Create shallow copy for safety
        _uiContext.Post(state => {
            var lines = (List<FilteredLogLine>)state!;
            ReplaceLogTextInternal(lines);
            UpdateSearchMatches();
        }, linesSnapshot);
    }

    private void ReplaceLogTextInternal(IReadOnlyList<FilteredLogLine> allLines)
    {
        if (_logEditorInstance?.Document == null) return;

        // Generate the full text string *once*
        // Use StringBuilder for efficiency with many lines
        var sb = new System.Text.StringBuilder();
        bool first = true;
        foreach (var line in allLines)
        {
            if (!first) { sb.Append(Environment.NewLine); }
            sb.Append(line.Text);
            first = false;
        }

        // Set the document text (must be on UI thread)
        _logEditorInstance.Document.Text = sb.ToString();

        // No auto-scroll on replace typically
    }

    // Core search logic - updates internal list and ruler markers
    private void UpdateSearchMatches()
    {
        string currentSearchTerm = SearchText;
        string textToSearch = GetCurrentDocumentText();
        ResetSearchState();
        if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
        {
            OnPropertyChanged(nameof(SearchStatusText));
            return;
        }

        int offset = 0;
        var tempMarkers = new List<SearchResult>();
        while (offset < textToSearch.Length)
        {
            int foundIndex = textToSearch.IndexOf(currentSearchTerm, offset, IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (foundIndex == -1) break;
            var newMatch = new SearchResult(foundIndex, currentSearchTerm.Length);
            _searchMatches.Add(newMatch);
            tempMarkers.Add(newMatch);
            offset = foundIndex + 1;
        }
        foreach (var marker in tempMarkers) SearchMarkers.Add(marker);
        OnPropertyChanged(nameof(SearchStatusText));
    }

    partial void OnIsCaseSensitiveSearchChanged(bool value)
    {
        UpdateSearchMatches();
        SaveCurrentSettingsDelayed();
    }

    public void LoadLogFromText(string text)
    {
        _logSource?.StopMonitoring(); // Stop current source
        _logFilterProcessor.Reset();
        LogDoc.Clear();
        FilteredLogLines.Clear();
        OnPropertyChanged(nameof(FilteredLogLinesCount));
        ResetSearchState();
        HighlightedFilteredLineIndex = -1;
        LogDoc.AddInitialLines(text); // Use existing storage
        // No need to manually start source here; TriggerFilterUpdate handles processing
        TriggerFilterUpdate();
        CurrentLogFilePath = "[Pasted Content]"; // Indicate non-file source
    }

    public void UpdateSearchIndexFromCharacterOffset(int characterOffset)
    {
        if (_searchMatches.Count == 0) return;

            // Find the match whose start offset is closest to the click offset
            int newIndex = _searchMatches
                .Select((match, index) => new { match, index, distance = Math.Abs(match.Offset - characterOffset) })
                .OrderBy(item => item.distance)
                .FirstOrDefault()?.index ?? -1;

        if (newIndex != -1 && newIndex != _currentSearchIndex)
        {
            _currentSearchIndex = newIndex;
            SelectAndScrollToCurrentMatch();
            OnPropertyChanged(nameof(SearchStatusText));
        }
    }

    private void HandleProcessorError(string contextMessage, Exception ex)
    {
        Debug.WriteLine($"{contextMessage}: {ex}");
        _uiContext.Post(_ => {
            CurrentBusyStates.Clear();
            MessageBox.Show($"Error processing logs: {ex.Message}", "Processing Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }, null);
    }

    // --- Highlighting Configuration ---
    private void TraverseFilterTreeForHighlighting(FilterViewModel filterViewModel, ObservableCollection<string> patterns)
    {
        if (!filterViewModel.Enabled) return;
        string? pattern = null;
        bool isRegex = false;
        if (filterViewModel.Filter is SubstringFilter sf && !string.IsNullOrEmpty(sf.Value))
        {
            pattern = Regex.Escape(sf.Value);
            isRegex = false;
        }
        else if (filterViewModel.Filter is RegexFilter rf && !string.IsNullOrEmpty(rf.Value))
        {
            pattern = rf.Value;
            isRegex = true;
        }
        if (pattern != null)
        {
            try
            {
                if (isRegex) { _ = new Regex(pattern); }
                if (!patterns.Contains(pattern))
                    patterns.Add(pattern);
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Invalid regex pattern skipped for highlighting: '{pattern}'. Error: {ex.Message}");
            }
    }
        foreach (var childFilter in filterViewModel.Children)
        {
            TraverseFilterTreeForHighlighting(childFilter, patterns);
        }
    }

    #endregion // --- Orchestration & Updates ---
    #region Jump To Line State
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JumpToLineCommand))]
    private string _targetOriginalLineNumberInput = string.Empty;

    [ObservableProperty]
    private string? _jumpStatusMessage;

    [ObservableProperty]
    private bool _isJumpTargetInvalid;
    #endregion

    #region // --- Lifecycle Management ---
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeProfileNameSubscription?.Dispose();
            _disposables.Dispose(); // Disposes processor, subscriptions, and log source
        }
    }

    // Called explicitly from the Window's Closing event.
    public void Cleanup()
    {
        // 1. Clear the busy state collection (good practice)
        _uiContext.Post(_ => CurrentBusyStates.Clear(), null);
        SaveCurrentSettings();
         _logSource?.StopMonitoring(); // Explicitly stop monitoring before disposing
        Dispose(); // Calls the main Dispose method which cleans everything else
    }
    #endregion // --- Lifecycle Management ---
}

/// <summary>
/// Represents the position and length of a found search match within the text.
/// Used for internal tracking and for markers on the OverviewRuler.
/// </summary>
public record SearchResult(int Offset, int Length);
