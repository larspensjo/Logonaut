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

namespace Logonaut.UI.ViewModels
{
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

        // --- Log Display Text Generation ---
        private bool _logTextUpdateScheduled = false;
        private readonly object _logTextUpdateLock = new object();

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
                LogDoc, // Pass the LogDocument instance - LogDoc owned by VM (UI State)
                _uiContext);

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
        public LogDocument LogDoc { get; } = new();

        // Collection of filtered lines currently displayed in the UI.
        public ObservableCollection<FilteredLogLine> FilteredLogLines { get; } = new();

        // Raw text content derived from FilteredLogLines, bound to AvalonEdit.
        [ObservableProperty]
        private string _logText = string.Empty;

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
            IsPerformingInitialLoad = true;

            try
            {
                _logFilterProcessor.Reset(); // Resets processor, queues debounced filter
                FilteredLogLines.Clear();
                OnPropertyChanged(nameof(FilteredLogLinesCount));
                ScheduleLogTextUpdate(UpdateType.Replace); // Clear editor text
                CurrentLogFilePath = selectedFile;

                await _logTailerService.ChangeFileAsync(selectedFile).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                IsBusyFiltering = false; // Reset on error
                IsPerformingInitialLoad = false; // Reset on error
                MessageBox.Show($"Error opening or monitoring log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentLogFilePath = null; // Reset state
                _logFilterProcessor.Reset(); // Ensure processor is reset on error too
                 // Ensure counts are visually reset on error too
                _uiContext.Post(_ => {
                    FilteredLogLines.Clear();
                    OnPropertyChanged(nameof(FilteredLogLinesCount));
                    TotalLogLines = 0; // Directly reset UI property
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
                string currentLogText = LogText; // Use local copy for safety
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

        /// <summary>
        /// Orchestrates the presentation of filtered log data, fulfilling Logonaut's core purpose.
        /// This method acts as the crucial bridge between the results generated by the background
        /// `LogFilterProcessor` and the user-facing UI state managed by this ViewModel.
        /// It receives processed `FilteredUpdate`s and is responsible for:
        ///   1. Updating the primary `FilteredLogLines` collection, which dictates the content
        ///      shown in the log viewer and the original line numbers.
        ///   2. Scheduling the generation of the `LogText` property bound to the AvalonEdit control,
        ///      ensuring the editor displays the correct filtered text.
        ///   3. Managing the application's busy indicator (`IsBusyFiltering`) to provide visual
        ///      feedback during potentially long `Replace` operations (like full re-filters or
        ///      initial loads).
        ///   4. Attempting to preserve the user's highlighted line selection across `Replace`
        ///      updates, maintaining user context during filter changes.
        /// </summary>
        /// <param name="update">The update containing new lines and the update type (Replace or Append).</param>
        private void ApplyFilteredUpdate(FilteredUpdate update)
        {
            bool wasReplace = update.Type == UpdateType.Replace;
            // To maintain user context, remember the original line number of the currently
            // highlighted line before modifying the displayed collection.
            int originalLineToRestore = HighlightedOriginalLineNumber;

            if (wasReplace)
            {
                // A Replace update signifies a complete refresh of the filtered view.
                // Clear the existing collection and replace it with the new lines.
                ReplaceFilteredLines(update.Lines);
            }
            else
            {
                // An Append update adds new lines to the end of the current view,
                // typically from new lines arriving in the tailed log file.
                AddFilteredLines(update.Lines);
            }

            // Updating the LogText bound to AvalonEdit involves potentially expensive string
            // manipulation and UI updates. Schedule this work to run after the FilteredLogLines
            // collection changes are complete to avoid race conditions and batch UI updates.
            ScheduleLogTextUpdate(update.Type);

            if (!wasReplace)
                return;

            // Post-update actions specific to Replace operations.

            // --- Restore Highlight ---
            // Attempt to re-select the line the user had highlighted before the Replace,
            // preserving their focus point across filter changes.
            if (originalLineToRestore > 0)
            {
                // Find the new index (if any) of the previously highlighted line.
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;

                // Queue the highlight update on the UI thread to ensure it happens after layout updates.
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = newIndex; }, null);
            }
            else
            {
                // Ensure any previous highlight is cleared if nothing was selected before.
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            // Check if this Replace corresponds to the completion of the initial load's filtering pass.
            if (IsPerformingInitialLoad)
            {
                // Yes, this is the first Replace after initiating the load.
                // Mark the initial load process as complete *and* turn off the busy indicator.
                _uiContext.Post(_ =>
                {
                    IsPerformingInitialLoad = false;
                    IsBusyFiltering = false;
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ---> Initial Load UI Update Complete. IsPerformingInitialLoad=false, IsBusyFiltering=false (Filtered results displayed)");
                }, null);
            }
            // If this Replace was triggered by a *subsequent* filter change (not initial load),
            // just turn off the busy indicator that was set when the change was triggered.
            else if (IsBusyFiltering) // Check IsBusyFiltering specifically
            {
                _uiContext.Post(_ => { IsBusyFiltering = false; }, null);
            }
        }

        // Helper methods called by ApplyFilteredUpdate to modify UI collections.
        private void AddFilteredLines(IReadOnlyList<FilteredLogLine> linesToAdd)
        {
            foreach (var line in linesToAdd)
            {
                FilteredLogLines.Add(line);
            }
            // ScheduleLogTextUpdate(); // Schedule is called by ApplyFilteredUpdate now
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
            // ScheduleLogTextUpdate(); // Schedule is called by ApplyFilteredUpdate now
        }

        private void ResetSearchState() {
            _searchMatches.Clear(); // Clear internal match list on replace
            SearchMarkers.Clear(); // Clear ruler markers
            _currentSearchIndex = -1; // Reset search index
            SelectAndScrollToCurrentMatch(); // Clear editor selection
        }

        // Schedules the LogText update to run after current UI operations.
        private void ScheduleLogTextUpdate(UpdateType triggeringUpdateType)
        {
            lock (_logTextUpdateLock)
            {
                if (!_logTextUpdateScheduled)
                {
                    _logTextUpdateScheduled = true;
                    _uiContext.Post(_ =>
                    {
                        lock (_logTextUpdateLock) { _logTextUpdateScheduled = false; }
                        UpdateLogTextInternal(); // Execute the update
                        if (IsAutoScrollEnabled && triggeringUpdateType == UpdateType.Append)
                        {
                             // Raise event for the view to handle scrolling
                            RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
                        }
                    }, null);
                }
            }
        }

        // Performs the actual update of the LogText property.
        private void UpdateLogTextInternal()
        {
            try
            {
                // Simple case - just join all lines
                LogText = string.Join(Environment.NewLine, FilteredLogLines.Select(line => line.Text));

                // Update search matches and markers AFTER LogText is updated
                UpdateSearchMatches();
            }
            catch (InvalidOperationException ioex) { Debug.WriteLine($"Error during UpdateLogTextInternal (Collection modified?): {ioex}"); }
            catch (Exception ex) { Debug.WriteLine($"Generic error during UpdateLogTextInternal: {ex}"); }
        }

        // Core search logic - updates internal list and ruler markers
        private void UpdateSearchMatches()
        {
             string currentSearchTerm = SearchText; // Use local copy
             string textToSearch = LogText; // Use local copy

             // Clear previous results
             ResetSearchState();

             if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
             {
                 OnPropertyChanged(nameof(SearchStatusText)); // Update status
                 return;
             }

             int offset = 0;
             var tempMarkers = new List<SearchResult>(); // Build temporary list for ruler
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
            LogText = string.Empty;      // Clear editor text via binding
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
}