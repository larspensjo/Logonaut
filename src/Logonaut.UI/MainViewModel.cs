using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Windows; // For MessageBox, Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Services; // Assuming IInputPromptService will be added here
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
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly ILogTailerService _logTailerService;
    private readonly SynchronizationContext _uiContext;

    // --- Core Processing Service ---
    private readonly ILogFilterProcessor _logFilterProcessor;

    // --- Lifecycle Management ---
    private readonly CompositeDisposable _disposables = new();

    // --- Search State & Ruler Markers ---
    private List<SearchResult> _searchMatches = new(); // Internal list
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
        ILogTailerService logTailerService,
        IFileDialogService? fileDialogService = null,
        ILogFilterProcessor? logFilterProcessor = null,
        SynchronizationContext? uiContext = null)
    {
        _settingsService = settingsService;
        _logTailerService = logTailerService;
        _fileDialogService = fileDialogService ?? new FileDialogService();
        _uiContext = uiContext ?? SynchronizationContext.Current ??
                        throw new InvalidOperationException("Could not capture or receive a valid SynchronizationContext."); // Store injected or captured

        // Initialize and own the processor
        _logFilterProcessor = logFilterProcessor ?? new LogFilterProcessor(
            _logTailerService,
            LogDoc,
            _uiContext,
            AddLineToLogDocument);

        // Subscribe to results from the processor
        var filterSubscription = _logFilterProcessor.FilteredUpdates
            .Subscribe(
                update => ApplyFilteredUpdate(update),
                ex => HandleProcessorError("Log Processing Error", ex)
            );

        var samplingScheduler = Scheduler.Default;
        _totalLinesSubscription = _logFilterProcessor.TotalLinesProcessed
            // Sample the stream, emitting the latest value every 200 milliseconds. Without a throttle, the UI thread can freeze up.
            .Sample(TimeSpan.FromMilliseconds(200), samplingScheduler)
            .ObserveOn(_uiContext) // Ensure handler runs on UI thread
            .Subscribe(
                count => ProcessTotalLinesUpdate(count), // Call dedicated method
                ex => HandleProcessorError("Total Lines Error", ex)
            );

        // --- Lifecycle Management ---
        _disposables.Add(_logFilterProcessor);
        _disposables.Add(filterSubscription);
        _disposables.Add(_totalLinesSubscription);

        // --- Initial State Setup ---
        Theme = new ThemeViewModel(); // Part of UI State
        LoadPersistedSettings(); // Load profiles and settings

        // Initial filter trigger is handled by setting ActiveFilterProfile in LoadPersistedSettings
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
            // Optional: Refocus the editor after jump?
            // RequestEditorFocus?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Line exists in original log but not in filtered view
            // OR line number is beyond the original log's total lines
            // (We can't easily distinguish without checking LogDoc count, which might be large)
            JumpStatusMessage = $"Line {targetLineNumber} not found in filtered view.";
            await TriggerInvalidInputFeedback();
            // Do NOT change HighlightedFilteredLineIndex
        }
    }

    private bool CanJumpToLine() => !string.IsNullOrWhiteSpace(TargetOriginalLineNumberInput);

    private async Task TriggerInvalidInputFeedback()
    {
        IsJumpTargetInvalid = true;
        // Keep the message for a few seconds
        await Task.Delay(2500); // Use Task.Delay
        // Clear message only if it hasn't been changed by a subsequent action
        if (IsJumpTargetInvalid)
        {
            IsJumpTargetInvalid = false;
            JumpStatusMessage = string.Empty;
        }
        // Note: IsJumpTargetInvalid is reset immediately in JumpToLine on next attempt
    }

    #endregion // Jump To Line Command

    #region // --- UI State Management ---
    // Holds observable properties representing the application's state for data binding.

    private void ProcessTotalLinesUpdate(long count)
    {
            TotalLogLines = count;
    }

    // Save setting when property changes
    partial void OnIsAutoScrollEnabledChanged(bool value)
    {
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
        // Optional: Trigger an initial full text update if needed when editor is first set
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
    private int _contextLines = 0; // Default to 0

    // Controls visibility of the custom line number margin.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomLineNumberMarginVisible))]
    private bool _showLineNumbers = true;
    public Visibility IsCustomLineNumberMarginVisible => ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

    partial void OnShowLineNumbersChanged(bool value)
    {
        SaveCurrentSettingsDelayed();
    }

    // Controls whether timestamp highlighting rules are applied in AvalonEdit.
    [ObservableProperty] private bool _highlightTimestamps = true;

    partial void OnHighlightTimestampsChanged(bool value)
    {
        SaveCurrentSettingsDelayed();
    }

    // Collection of filter patterns (substrings/regex) for highlighting.
    // Note: This state is derived by traversing the *active* FilterProfile.
    [ObservableProperty] private ObservableCollection<string> _filterSubstrings = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProcessingIndicatorVisible))] // Notify combined property
    private bool _isBusyFiltering = false;         // The MainViewModel object is in a busy state.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProcessingIndicatorVisible))] // Notify combined property
    private bool _isPerformingInitialLoad = false; // Flag to track initial file load state

    public bool IsProcessingIndicatorVisible => IsBusyFiltering || IsPerformingInitialLoad;

    private IDisposable? _totalLinesSubscription;

    // Controls whether search is case sensitive
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private bool _isCaseSensitiveSearch = false;

    /// <summary>
    /// The filter node currently selected within the TreeView of the ActiveFilterProfile.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))] // Enable adding if composite selected
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))] // Enable removing selected node
    [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))] // Enable editing selected node
    private FilterViewModel? _selectedFilterNode;

    /// <summary>
    /// Collection exposed specifically for the TreeView's ItemsSource.
    /// Contains only the root node of the ActiveFilterProfile's tree.
    /// </summary>
    public ObservableCollection<FilterViewModel> ActiveTreeRootNodes { get; } = new();

    // --- Persistence Methods ---
    private void LoadPersistedSettings()
    {
        LogonautSettings settings;
        settings = _settingsService.LoadSettings();

        // Apply loaded settings to ViewModel properties
        // TODO: Apply theme based on settings.LastTheme
        LoadFilterProfiles(settings);
        ShowLineNumbers = settings.ShowLineNumbers;
        HighlightTimestamps = settings.HighlightTimestamps;
        IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;
        ContextLines = settings.ContextLines;
        IsAutoScrollEnabled = settings.AutoScrollToTail;
    }

    private void SaveCurrentSettingsDelayed() {
        _uiContext.Post(_ => { SaveCurrentSettings(); }, null);
    }

    private void SaveCurrentSettings()
    {
        var settingsToSave = new LogonautSettings
        {
            // --- Other settings ---
            ContextLines = this.ContextLines,
            ShowLineNumbers = this.ShowLineNumbers,
            HighlightTimestamps = this.HighlightTimestamps,
            IsCaseSensitiveSearch = this.IsCaseSensitiveSearch,
            AutoScrollToTail = this.IsAutoScrollEnabled,
            // TODO: Add theme, window state etc.
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
        if (ActiveFilterProfile == null)
            throw new InvalidOperationException("No active profile"); // Should not happen if CanExecute is right

        if (ActiveFilterProfile.RootFilterViewModel == null && SelectedFilterNode == null)
        {
            // No profile active OR tree is empty and nothing selected - target the profile root
            if (ActiveFilterProfile == null) return; // Should not happen if CanExecute is right
        }
        else if (SelectedFilterNode != null && !(SelectedFilterNode.Filter is CompositeFilter))
        {
            throw new InvalidOperationException("Selected node is not composite"); // Should not happen if CanExecute is right
        }

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
            // TriggerFilterUpdate() is handled by callbacks within AddChildFilter/SetModelRootFilter
        SaveCurrentSettingsDelayed();
    }

    // Can add if a profile is active. Adding logic handles where it goes.
    private bool CanAddFilterNode()
    {
        // Condition 0: Must have an active profile
        if (ActiveFilterProfile == null)
            return false;

        // Condition A: Active profile's tree is empty?
        bool isTreeEmpty = ActiveFilterProfile.RootFilterViewModel == null;

        // Condition B: Is a composite node selected?
        bool isCompositeNodeSelected = SelectedFilterNode != null &&
                                        SelectedFilterNode.Filter is CompositeFilter; // Check if the *selected node's model* is composite

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
            ActiveFilterProfile.SetModelRootFilter(null); // Clears the model and VM tree
            UpdateActiveTreeRootNodes(ActiveFilterProfile);
            SelectedFilterNode = null; // Clear selection
        }
        // Case 2: Removing a child node
        else if (parent != null)
        {
            var nodeToRemove = SelectedFilterNode; // Keep reference
            SelectedFilterNode = parent; // Select parent *before* removing child
            parent.RemoveChild(nodeToRemove); // RemoveChild uses callback internally
        }
        // TriggerFilterUpdate(); // Handled by callbacks
        SaveCurrentSettingsDelayed();
    }
    private bool CanRemoveFilterNode() => SelectedFilterNode != null && ActiveFilterProfile != null;

    [RelayCommand(CanExecute = nameof(CanToggleEditNode))]
    private void ToggleEditNode()
    {
        if (SelectedFilterNode?.IsEditable ?? false)
        {
            if (SelectedFilterNode.IsNotEditing)
                SelectedFilterNode.BeginEditCommand.Execute(null);
            else {
                SelectedFilterNode.EndEditCommand.Execute(null); // EndEdit uses callback, which triggers save
                // Save settings after editing is complete
                SaveCurrentSettingsDelayed();
            }
        }
    }
    private bool CanToggleEditNode() => SelectedFilterNode?.IsEditable ?? false;

    // === Other Commands (File, Search, Context Lines) ===
    // Ensure they interact correctly with the Active Profile concept if needed

    [RelayCommand] private async Task OpenLogFileAsync()
    {
        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
        if (string.IsNullOrEmpty(selectedFile)) return;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: '{selectedFile}'");

        IsBusyFiltering = true;
        IsPerformingInitialLoad = true; // Set flags before async work

        try
        {
            // 1. Reset Processor State (sets internal flag)
            _logFilterProcessor.Reset();

            // 2. Clear ViewModel/UI State
            FilteredLogLines.Clear();
            OnPropertyChanged(nameof(FilteredLogLinesCount));
            ScheduleLogTextUpdate(FilteredLogLines); // Clear editor
            CurrentLogFilePath = selectedFile; // Update displayed path

            // 3. Perform Initial Read & Start Tailing (populates LogDoc)
            LogDoc.Clear(); // Clear the document before reading
            long initialLines = await _logTailerService.ChangeFileAsync(selectedFile, AddLineToLogDocument).ConfigureAwait(true);

            // 4. Update Total Lines Display immediately after initial read
                _uiContext.Post(_ => TotalLogLines = initialLines, null); // Use UI context

            // 5. Trigger the First Filter Explicitly
            IFilter? firstFilter = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
            _logFilterProcessor.UpdateFilterSettings(firstFilter, ContextLines);
            // Update highlighting rules based on the active filter
            UpdateFilterSubstrings();

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: ChangeFileAsync completed ({initialLines} lines). First filter triggered.");
            // Busy flags (IsBusyFiltering, IsPerformingInitialLoad) will be reset
            // by ApplyFilteredUpdate when it receives the Replace update from the processor.
        }
        catch (Exception ex)
        {
            // Reset flags on error
            IsBusyFiltering = false;
            IsPerformingInitialLoad = false;
            MessageBox.Show($"Error opening or reading log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
            CurrentLogFilePath = null; // Clear path
            // Reset processor again to be safe? Maybe not needed if Reset only sets flag.
            // Clear UI collections again
            _uiContext.Post(_ => {
                    FilteredLogLines.Clear();
                    OnPropertyChanged(nameof(FilteredLogLinesCount));
                TotalLogLines = 0; // Reset total lines display
                }, null);
        }
    }

    // TODO: We want Shift+F3 to trigger previous search
    [RelayCommand(CanExecute = nameof(CanSearch))] private void PreviousSearch()
    {
        if (_searchMatches.Count == 0) return;

        // Handle initial case explicitly for intuitive wrap-around
        if (_currentSearchIndex == -1)
            _currentSearchIndex = _searchMatches.Count - 1; // Go to the last match
        else
            _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count; // Standard wrap-around
        SelectAndScrollToCurrentMatch();
        OnPropertyChanged(nameof(SearchStatusText));
    }

    // TODO: We want F3 to trigger next search
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

            // Need LogText to map offset to line
            string currentLogText = GetCurrentDocumentText(); // Use local copy for safety
            if (!string.IsNullOrEmpty(currentLogText) && CurrentMatchOffset >= 0 && CurrentMatchOffset < currentLogText.Length)
            {
                // This requires creating a temporary TextDocument to use GetLineByOffset reliably
                // Alternatively, approximate by counting newlines (less robust).
                // Let's assume we can access the editor's document directly or create one.
                // **Simplification for now: Search FilteredLogLines. This is less performant.**
                // TODO: A better way involves direct access to AvalonEdit's Document in MainWindow.xaml.cs
                // or passing the AvalonEdit control instance to the ViewModel (less ideal).

                // Find the line number containing the start of the match
                int lineIndex = FindFilteredLineIndexContainingOffset(CurrentMatchOffset);
                HighlightedFilteredLineIndex = lineIndex; // Update the highlight
            }
            else
            {
                HighlightedFilteredLineIndex = -1; // Invalid offset
            }
        }
        else
        {
            CurrentMatchOffset = -1;
            CurrentMatchLength = 0;
            HighlightedFilteredLineIndex = -1; // Clear highlight if no match selected
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
            // Check if offset falls within this line (inclusive of start, exclusive of end newline)
            // Add + Environment.NewLine.Length for the newline characters between lines
            int lineEndOffset = currentOffset + lineLength;
            if (offset >= currentOffset && offset <= lineEndOffset) // <= includes position right after last char
            {
                return i;
            }
            currentOffset = lineEndOffset + Environment.NewLine.Length; // Move to start of next line
        }
        return -1; // Offset not found
    }

    partial void OnSearchTextChanged(string value) => UpdateSearchMatches(); // Trigger search update

    private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
    {
        var newFilterSubstrings = new ObservableCollection<string>();
        // Traverse the tree of the *currently active* profile
        if (ActiveFilterProfile?.RootFilterViewModel != null)
            TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterSubstrings);
        FilterSubstrings = newFilterSubstrings; // Update the property bound to AvalonEditHelper
    }

    [RelayCommand(CanExecute = nameof(CanDecrementContextLines))]
    private void DecrementContextLines()
    {
        ContextLines = Math.Max(0, ContextLines - 1);
        // OnContextLinesChanged triggers TriggerFilterUpdate
    }
    private bool CanDecrementContextLines() => ContextLines > 0;

    [RelayCommand] private void IncrementContextLines()
    {
        ContextLines++;
        // OnContextLinesChanged triggers TriggerFilterUpdate
    }

    partial void OnContextLinesChanged(int value)
    {
        DecrementContextLinesCommand.NotifyCanExecuteChanged();
        TriggerFilterUpdate(); // Trigger re-filter when context changes
        SaveCurrentSettingsDelayed();
    }

    #endregion // --- Command Handling ---

    #region // --- Orchestration & Updates ---

    private void AddLineToLogDocument(string line)
    {
        // This method will be called by the processor, potentially on a background thread.
        // LogDocument handles its own locking.
        LogDoc.AppendLine(line);
        // Optional: Could add tracing here if needed
        // System.Diagnostics.Debug.WriteLine($"---> MainViewModel: Added line to LogDoc via callback: {line.Substring(0, Math.Min(line.Length, 20))}");
    }

    /// <summary>
    /// Updates the ActiveTreeRootNodes collection based on the new active profile.
    /// </summary>
    private void UpdateActiveTreeRootNodes(FilterProfileViewModel? activeProfile)
    {
        ActiveTreeRootNodes.Clear();
        if (activeProfile?.RootFilterViewModel != null)
            ActiveTreeRootNodes.Add(activeProfile.RootFilterViewModel);

        // TODO: There will be an automatic CollectionChanged event, not a PropertyChanged event. However, we shouldn't need a PropertyChanged event here.
        // The UI controls bound via ItemsSource listen for CollectionChanged.
        OnPropertyChanged(nameof(ActiveTreeRootNodes));
    }

    // Central method to signal the processor that filters or context may have changed.
    // Called by:
    // - OnActiveFilterProfileChanged
    // - OnContextLinesChanged
    // - Callbacks passed to FilterViewModel instances (triggered by Enable toggle, EndEdit)
    // - Callbacks from FilterProfileViewModel (e.g., SetModelRootFilter)
    // TODO: Whenever the filter settings are changed, we should ensure the line currently selected in the editor is still visible. Preferably at the same window position.
    private void TriggerFilterUpdate()
    {
        IsBusyFiltering = true; // Set IsBusyFiltering = true; before starting to work.
        IsPerformingInitialLoad = false; // Explicit filter changes are not initial loads
        _uiContext.Post(_ =>
        {
            // Get the filter model from the *currently selected* profile VM
            IFilter? filterToApply = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();

            // Send the filter and context lines to the background processor
            _logFilterProcessor.UpdateFilterSettings(filterToApply, ContextLines);

            // Update highlighting rules based on the *active* filter tree
            UpdateFilterSubstrings();
        }, null);
    }

    private string GetCurrentDocumentText()
    {
        // Priority 1: Use the actual editor document if available (running in UI)
        // Reading Text property is generally thread-safe.
        if (_logEditorInstance?.Document != null)
        {
            return _logEditorInstance.Document.Text;
        }

        // Priority 2: Fallback for testing or before editor is initialized.
        // Simulate the document content by joining the FilteredLogLines.
        // This allows internal logic like search to work correctly in unit tests.
        if (FilteredLogLines.Any())
        {
            // Use StringBuilder for potentially large test data sets too
            var sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (var line in FilteredLogLines)
            {
                if (!first) { sb.Append(Environment.NewLine); }
                sb.Append(line.Text);
                first = false;
            }
            return sb.ToString();
        }

        // Default: Return empty if editor not set AND no filtered lines
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

        // Check if this update represents an append operation relative to the current state
        bool isAppend = CheckIfAppend(update.Lines);

        if (isAppend)
        {
            // --- Handle Append ---
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate: Detected Append.");

            // 1. Calculate the newly added lines
            var appendedLines = update.Lines.Skip(FilteredLogLines.Count).ToList();

            // 2. Add only the new lines to the ObservableCollection
            AddFilteredLines(appendedLines); // This notifies controls bound to the collection

            // 3. Schedule the text append operation for AvalonEdit
            ScheduleLogTextAppend(appendedLines);

            // 4. Trigger Auto-Scroll if enabled
            if (IsAutoScrollEnabled)
            {
                RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> ApplyFilteredUpdate (Append): AutoScroll Triggered.");
            }

            // 5. Reset BusyFiltering (Append means this batch is done)
            //    Do NOT reset IsPerformingInitialLoad on Append.
            _uiContext.Post(_ => {
                IsBusyFiltering = false;
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> ApplyFilteredUpdate (Append): UI Update Complete. BusyFiltering=false.");
            }, null);
        }
        else
        {
            // --- Handle Replace
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate: Detected Replace.");
            int originalLineToRestore = HighlightedOriginalLineNumber;

            // 1. Replace the entire ObservableCollection
            ReplaceFilteredLines(update.Lines); // Clears and adds new lines

            // 2. Schedule the full text replace for AvalonEdit
            ScheduleLogTextUpdate(FilteredLogLines); // Passes the complete new list

            // 3. Restore Highlight (only makes sense after a replace)
            if (originalLineToRestore > 0)
            {
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;
                // Ensure highlight update runs after potential layout passes
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = newIndex; }, null);
            } else {
                // Clear highlight if nothing was selected before
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            // 4. Reset Busy Indicators for Replace
            _uiContext.Post(_ =>
            {
                IsPerformingInitialLoad = false; // Reset InitialLoad on Replace
                IsBusyFiltering = false;         // Reset BusyFiltering on Replace
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> ApplyFilteredUpdate (Replace): UI Update Complete. Flags Reset.");
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

        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!;
            AppendLogTextInternal(lines);
            // Update search matches *after* appending text
            UpdateSearchMatches();
        }, linesSnapshot);
    }

    private void AppendLogTextInternal(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        // Ensure editor and document exist and there's something to append
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
                {
                    sb.Append(Environment.NewLine);
                }
                sb.Append(linesToAppend[i].Text);
            }

            string textToAppend = sb.ToString();

            _logEditorInstance.Document.Insert(_logEditorInstance.Document.TextLength, textToAppend);
        }
        catch (Exception ex)
        {
            // Log or handle potential errors during text manipulation
            Debug.WriteLine($"Error appending text to AvalonEdit: {ex.Message}");
            // Optionally, fall back to a full replace if append fails?
        }
    }

    // Helper methods called by ApplyFilteredUpdate to modify UI collections.
    private void AddFilteredLines(IReadOnlyList<FilteredLogLine> linesToAdd)
    {
        foreach (var line in linesToAdd)
        {
            FilteredLogLines.Add(line);
        }
        OnPropertyChanged(nameof(FilteredLogLinesCount));
    }

    private void ReplaceFilteredLines(IReadOnlyList<FilteredLogLine> newLines)
    {
        ResetSearchState();
        FilteredLogLines.Clear();

        foreach (var line in newLines)
        {
            FilteredLogLines.Add(line);
        }
        OnPropertyChanged(nameof(FilteredLogLinesCount));
    }

    private void ResetSearchState() {
        _searchMatches.Clear(); // Clear internal match list on replace
        SearchMarkers.Clear(); // Clear ruler markers
        _currentSearchIndex = -1; // Reset search index
        SelectAndScrollToCurrentMatch(); // Clear editor selection
    }

    // Schedules the LogText update to run after current UI operations.
    private void ScheduleLogTextUpdate(IReadOnlyList<FilteredLogLine> relevantLines)
    {
        // Clone relevantLines if it might be modified elsewhere before Post executes?
        // ObservableCollection snapshot for Replace is safe. List passed for Append is safe.
        var linesSnapshot = relevantLines.ToList(); // Create shallow copy for safety

        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!;

            ReplaceLogTextInternal(lines);
            UpdateSearchMatches();

        }, linesSnapshot); // Pass only lines as state
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
        string fullText = sb.ToString();

        // Set the document text (must be on UI thread)
        _logEditorInstance.Document.Text = fullText;

        // No auto-scroll on replace typically
    }

    // Core search logic - updates internal list and ruler markers
    private void UpdateSearchMatches()
    {
        string currentSearchTerm = SearchText;
        string textToSearch = GetCurrentDocumentText();

        ResetSearchState(); // Clears internal list, markers, index, selection

        if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
        {
            OnPropertyChanged(nameof(SearchStatusText));
            return;
        }

        int offset = 0;
        var tempMarkers = new List<SearchResult>();
        while (offset < textToSearch.Length)
        {
            int foundIndex = textToSearch.IndexOf(
                currentSearchTerm,
                offset,
                IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (foundIndex == -1) break;
            var newMatch = new SearchResult(foundIndex, currentSearchTerm.Length);
                _searchMatches.Add(newMatch); // Add to internal list
                tempMarkers.Add(newMatch);    // Add to temp list for ruler batch update

                offset = foundIndex + 1; // Continue search after the start of the current match
        }

            // Batch update the observable collection for the ruler for potentially better perf
            foreach (var marker in tempMarkers)
            {
                SearchMarkers.Add(marker);
            }

            OnPropertyChanged(nameof(SearchStatusText)); // Update match count display
    }

    partial void OnIsCaseSensitiveSearchChanged(bool value)
    {
        UpdateSearchMatches(); // This already happened due to [NotifyPropertyChangedFor] equivalent
        SaveCurrentSettingsDelayed();
    }

    public void LoadLogFromText(string text)
    {
        _logFilterProcessor.Reset(); // Reset processor
        LogDoc.Clear();              // Clear internal document storage
        FilteredLogLines.Clear();    // Clear UI collection
        OnPropertyChanged(nameof(FilteredLogLinesCount)); // Notify count changed
        _searchMatches.Clear();      // Clear search state
        SearchMarkers.Clear();
        _currentSearchIndex = -1;
        HighlightedFilteredLineIndex = -1; // Reset highlight

        LogDoc.AddInitialLines(text); // Add new lines to storage

        // Trigger filter update to process the new content using the active profile
        TriggerFilterUpdate();
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
            // If clicking within the currently selected match, maybe don't change index? (Current logic selects closest)
    }

    private void HandleProcessorError(string contextMessage, Exception ex)
    {
        Debug.WriteLine($"{contextMessage}: {ex}");
        _uiContext.Post(_ =>
        {
            IsBusyFiltering = false; // Ensure busy indicator is off
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
            pattern = Regex.Escape(sf.Value); // Escape for regex highlighting
            isRegex = false;
        }
        else if (filterViewModel.Filter is RegexFilter rf && !string.IsNullOrEmpty(rf.Value))
        {
            pattern = rf.Value; // Use raw regex pattern
            isRegex = true;
        }

        if (pattern != null)
        {
            try
            {
                if (isRegex) { _ = new Regex(pattern); } // Throws if invalid
                if (!patterns.Contains(pattern)) // Avoid duplicates
                    patterns.Add(pattern);
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Invalid regex pattern skipped for highlighting: '{pattern}'. Error: {ex.Message}");
                // TODO: Optionally provide UI feedback about the invalid regex in the filter.
            }
        }

        // Recurse through children
        foreach (var childFilter in filterViewModel.Children)
        {
            TraverseFilterTreeForHighlighting(childFilter, patterns);
        }
    }

    #endregion // --- Orchestration & Updates ---
    
    #region Jump To Line State

    // Input string from the TextBox, bound TwoWay
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JumpToLineCommand))]
    private string _targetOriginalLineNumberInput = string.Empty;

    // Status message for jump feedback (e.g., "Line not found")
    [ObservableProperty]
    private string? _jumpStatusMessage;

    // Used to trigger visual feedback (e.g., flash background red)
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
            _activeProfileNameSubscription?.Dispose(); // Dispose the name listener
            _disposables.Dispose(); // Disposes processor and subscriptions
        }
    }

    // Called explicitly from the Window's Closing event.
    public void Cleanup()
    {
        IsBusyFiltering = false; // Ensure busy indicator is hidden on exit
        SaveCurrentSettings();   // Save state before disposing
        _logTailerService?.StopTailing();
        Dispose();               // Dispose resources
    }
    #endregion // --- Lifecycle Management ---
}

/// <summary>
/// Represents the position and length of a found search match within the text.
/// Used for internal tracking and for markers on the OverviewRuler.
/// </summary>
public record SearchResult(int Offset, int Length);
