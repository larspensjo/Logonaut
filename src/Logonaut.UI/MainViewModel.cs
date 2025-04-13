using System; 
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq; 
using System.Reactive.Disposables; 
using System.Text.RegularExpressions;
using System.Text;
using System.Threading; 
using System.Windows; 
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;         
using Logonaut.Filters;
using Logonaut.LogTailing;
using Logonaut.UI.Services;

namespace Logonaut.UI.ViewModels
{
    // MainViewModel now acts as a true mediator between the View, the UI data structures (FilterViewModel tree), and the core processing logic (ILogFilterProcessor).
    // It focuses on managing the UI state, handling user input via commands, and coordinating the flow of data and triggers between the UI and the background processing service,
    // without containing the complex reactive pipeline logic itself.
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        #region // --- Fields ---

        // --- UI Services & Context ---
        private readonly IFileDialogService _fileDialogService;
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
        [ObservableProperty]
        private ObservableCollection<SearchResult> _searchMarkers = new();

        #endregion // --- Fields ---

        #region // --- Constructor ---

        public MainViewModel(IFileDialogService? fileDialogService = null, ILogFilterProcessor? logFilterProcessor = null)
        {
            // --- Interaction with UI Services ---
            _fileDialogService = fileDialogService ?? new FileDialogService();
            _uiContext = SynchronizationContext.Current ?? throw new InvalidOperationException("Could not capture SynchronizationContext. Ensure ViewModel is created on the UI thread.");

            // --- Orchestration with ILogFilterProcessor ---
            // Initialize and own the processor
            _logFilterProcessor = logFilterProcessor ?? new LogFilterProcessor(
                LogTailerManager.Instance.LogLines,
                LogDoc, // Pass the LogDocument instance - LogDoc owned by VM (UI State)
                _uiContext);

            // Subscribe to results from the processor
            var filterSubscription = _logFilterProcessor.FilteredUpdates
                .Subscribe(
                    update => ApplyFilteredUpdate(update),
                    ex => HandleProcessorError("Log Processing Error", ex)
                );

            // --- Lifecycle Management ---
            _disposables.Add(_logFilterProcessor);
            _disposables.Add(filterSubscription);

            LoadPersistedSettings();

            // --- Initial State Setup ---
            Theme = new ThemeViewModel(); // Part of UI State
            TriggerFilterUpdate(); // Initial filter application
        }

        #endregion // --- Constructor ---

        #region // --- UI State Management ---
        // Holds observable properties representing the application's state for data binding.

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
        [ObservableProperty]
        private int _currentMatchOffset = -1;
        [ObservableProperty]
        private int _currentMatchLength = 0;

        // Status text for search
        public string SearchStatusText
        {
            get
            {
                if (string.IsNullOrEmpty(SearchText)) return "";
                // Use _searchMatches for status count, SearchMarkers is just for the ruler display
                if (_searchMatches.Count == 0) return "Phrase not found";
                if (_currentSearchIndex == -1) return $"{_searchMatches.Count} matches found";
                return $"Match {_currentSearchIndex + 1} of {_searchMatches.Count}";
            }
        }

        // Path of the currently monitored log file.
        [ObservableProperty]
        private string? _currentLogFilePath;

        // Hierarchical structure of FilterViewModel objects for the filter tree UI.
        public ObservableCollection<FilterViewModel> FilterProfiles { get; } = new();

        // Currently selected node in the filter tree UI.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveFilterCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleEditCommand))]
        private FilterViewModel? _selectedFilter;

        // Configured number of context lines to display around filter matches.
        [ObservableProperty]
        private int _contextLines = 0; // Default to 0

        // Controls visibility of the custom line number margin.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomLineNumberMarginVisible))]
        private bool _showLineNumbers = true;
        public Visibility IsCustomLineNumberMarginVisible => ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

        // Controls whether timestamp highlighting rules are applied in AvalonEdit.
        [ObservableProperty]
        private bool _highlightTimestamps = true;

        // Collection of filter patterns (substrings/regex) for highlighting.
        // Note: This state is derived by traversing FilterProfiles (see Highlighting Configuration).
        [ObservableProperty]
        private ObservableCollection<string> _filterSubstrings = new();

        [ObservableProperty]
        private bool _isBusyFiltering = false;

        // Controls whether search is case sensitive
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SearchStatusText))]
        private bool _isCaseSensitiveSearch = false;

        private void LoadPersistedSettings()
        {
            LogonautSettings settings;
            try
            {
                settings = SettingsManager.LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Apply loaded settings to ViewModel properties
            // Use SetProperty directly or let ObservableProperty handle notifications
            ContextLines = settings.ContextLines;
            ShowLineNumbers = settings.ShowLineNumbers;
            HighlightTimestamps = settings.HighlightTimestamps;
            IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;
            // TODO: Apply theme based on settings.LastTheme

            // Rebuild the FilterProfiles collection from the loaded filter
            FilterProfiles.Clear(); // Clear any default/existing
            if (settings.RootFilter != null)
            {
                // The callback passed here ensures changes trigger updates later
                var rootFilterViewModel = new FilterViewModel(settings.RootFilter, TriggerFilterUpdate);
                FilterProfiles.Add(rootFilterViewModel);
                // Optionally select the root filter automatically
                // SelectedFilter = rootFilterViewModel;
            }
            // If settings.RootFilter was null, FilterProfiles remains empty.
        }

        private void SaveCurrentSettings()
        {
            // Create a settings object from the current ViewModel state
            var settingsToSave = new LogonautSettings
            {
                RootFilter = GetCurrentFilter(), // Get the current filter tree
                ContextLines = this.ContextLines,
                ShowLineNumbers = this.ShowLineNumbers,
                HighlightTimestamps = this.HighlightTimestamps,
                IsCaseSensitiveSearch = this.IsCaseSensitiveSearch,
                // TODO: Add theme, window state etc.
            };

            try
            {
                SettingsManager.SaveSettings(settingsToSave);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion // --- UI State Management ---

        #region // --- Command Handling ---
        // Defines RelayCommands executed in response to user interactions.

        // --- Filter Manipulation Commands ---
        [RelayCommand] private void AddSubstringFilter() => AddFilter(new SubstringFilter(""));
        [RelayCommand] private void AddRegexFilter() => AddFilter(new RegexFilter(".*"));
        [RelayCommand] private void AddAndFilter() => AddFilter(new AndFilter());
        [RelayCommand] private void AddOrFilter() => AddFilter(new OrFilter());
        [RelayCommand] private void AddNorFilter() => AddFilter(new NorFilter());

        [RelayCommand(CanExecute = nameof(CanRemoveFilter))]
        private void RemoveFilter()
        {
            if (SelectedFilter == null) return;

            FilterViewModel? parent = SelectedFilter.Parent;
            bool removed = false;

            if (FilterProfiles.Contains(SelectedFilter))
            {
                FilterProfiles.Remove(SelectedFilter);
                removed = true;
                SelectedFilter = null; // Clear selection if root was removed
            }
            else if (parent != null)
            {
                parent.RemoveChild(SelectedFilter); // RemoveChild internally uses callback
                removed = true;
                SelectedFilter = parent; // Select parent after removing child
            }

            if (removed)
            {
                TriggerFilterUpdate(); // Orchestration: Signal processor after change
            }
        }
        private bool CanRemoveFilter() => SelectedFilter != null;

        [RelayCommand(CanExecute = nameof(CanToggleEdit))]
        private void ToggleEdit()
        {
            // Logic relies on FilterViewModel's BeginEdit/EndEdit which uses the callback
            // to trigger TriggerFilterUpdate indirectly.
            if (SelectedFilter?.IsEditable ?? false)
            {
                if (SelectedFilter.IsNotEditing)
                    SelectedFilter.BeginEdit();
                else
                    SelectedFilter.EndEdit();
            }
        }
        private bool CanToggleEdit() => SelectedFilter?.IsEditable ?? false;

        // --- File Handling Command ---
        [RelayCommand]
        private void OpenLogFile()
        {
            // Interaction with UI Services:
            string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
            if (string.IsNullOrEmpty(selectedFile))
                return;

            // Orchestration & State Update:
            _logFilterProcessor.Reset(); // Reset processor first
            FilteredLogLines.Clear();    // Clear UI collection
            ScheduleLogTextUpdate();     // Update text display to empty
            CurrentLogFilePath = selectedFile; // Update state

            try
            {
                // Interaction with Log Tailing Service:
                LogTailerManager.Instance.ChangeFile(selectedFile);

                // After successfully changing the file and resetting the processor,
                // tell the processor to apply the filters currently defined in the UI.
                TriggerFilterUpdate();
            }
            catch (Exception ex)
            {
                IsBusyFiltering = false; // Hide busy on error
                MessageBox.Show($"Error opening or monitoring log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentLogFilePath = null; // Reset state on error
                _logFilterProcessor.Reset(); // Also reset processor
            }
        }

        // --- Search Commands ---
        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void PreviousSearch()
        {
            if (_searchMatches.Count == 0) return;

            _currentSearchIndex--;
            if (_currentSearchIndex < 0)
            {
                _currentSearchIndex = _searchMatches.Count - 1; // Wrap around to the end
            }
            SelectAndScrollToCurrentMatch();
            OnPropertyChanged(nameof(SearchStatusText)); // Update status
        }

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch()
        {
            if (_searchMatches.Count == 0) return;

            _currentSearchIndex++;
            if (_currentSearchIndex >= _searchMatches.Count)
            {
                _currentSearchIndex = 0; // Wrap around to the start
            }
            SelectAndScrollToCurrentMatch();
            OnPropertyChanged(nameof(SearchStatusText)); // Update status
        }
        private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

        // Helper to update selection properties based on current index
        private void SelectAndScrollToCurrentMatch()
        {
            if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchMatches.Count)
            {
                var match = _searchMatches[_currentSearchIndex];
                // Update properties bound to AvalonEditHelper
                CurrentMatchOffset = match.Offset;
                CurrentMatchLength = match.Length;
            }
            else
            {
                // Clear selection if index is invalid
                CurrentMatchOffset = -1;
                CurrentMatchLength = 0;
            }
        }

        // Trigger search update when SearchText changes
        partial void OnSearchTextChanged(string value)
        {
            // Delay slightly or use async if searching becomes slow
            UpdateSearchMatches();
        }

        // --- Highlighting Command ---
        [RelayCommand]
        private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
        {
            var newFilterSubstrings = new ObservableCollection<string>();
            if (FilterProfiles.Count > 0)
            {
                TraverseFilterTreeForHighlighting(FilterProfiles[0], newFilterSubstrings);
            }
            FilterSubstrings = newFilterSubstrings; // Update the UI State property
        }

        #endregion // --- Command Handling ---

        #region // --- Filter Tree Management (UI Representation) ---
        // Manages the FilterProfiles collection and interaction logic for the filter tree UI.

        // Helper method used by Add*Filter commands.
        private void AddFilter(IFilter filter)
        {
            // Creates the ViewModel, passing the callback for orchestration.
            var newFilterVM = new FilterViewModel(filter, TriggerFilterUpdate);

            if (FilterProfiles.Count == 0)
            {
                FilterProfiles.Add(newFilterVM);
                SelectedFilter = newFilterVM; // Update UI State: Auto-select the new root
            }
            else if (SelectedFilter != null && SelectedFilter.FilterModel is CompositeFilter)
            {
                // AddChildFilter internally adds to FilterViewModel's Children collection.
                SelectedFilter.AddChildFilter(filter);
                // We want composite filter to be expanded when a child is added.
                SelectedFilter.IsExpanded = true;
            }
            else
            {
                // UI Feedback / Interaction Logic:
                MessageBox.Show(
                    SelectedFilter == null
                    ? "Please select a composite filter node (And, Or, Nor) first to add a child."
                    : "Selected filter is not a composite filter (And, Or, Nor). Cannot add a child filter here.",
                    "Add Filter Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return; // Don't trigger update if nothing was added
            }
            // Orchestration: Signal processor after structure change.
            TriggerFilterUpdate();
        }

        // Handles changes to the SelectedFilter property (UI State).
        partial void OnSelectedFilterChanged(FilterViewModel? oldValue, FilterViewModel? newValue)
        {
            // Ensure edit mode is ended if selection moves away.
            if (oldValue != null && oldValue.IsEditing)
            {
                oldValue.EndEditCommand.Execute(null);
            }
            // Update IsSelected state on ViewModels for visual feedback (if needed by template).
            if (oldValue != null) oldValue.IsSelected = false;
            if (newValue != null) newValue.IsSelected = true;
        }

        #endregion // --- Filter Tree Management (UI Representation) ---

        #region // --- Orchestration with ILogFilterProcessor ---
        // Coordinates interactions with the background filtering service.

        // Called by the processor subscription to apply incoming updates.
        private void ApplyFilteredUpdate(FilteredUpdate update)
        {
            bool wasReplace = update.Type == UpdateType.Replace;

            if (wasReplace)
            {
                ReplaceFilteredLines(update.Lines); // Updates UI State (FilteredLogLines)
                SearchMarkers.Clear(); // Clear markers when replacing content
                _searchMatches.Clear(); // Clear internal match list
                _currentSearchIndex = -1; // Reset search index
            }
            else // Append
            {
                AddFilteredLines(update.Lines); // Updates UI State (FilteredLogLines)
                // Appending might invalidate existing marker offsets if done simply.
                // For now, we'll let the next search update handle markers correctly.
                // TODO: A more advanced approach might try to update marker offsets, but
                // re-running search on the updated LogText is safer.
            }

            // Schedule the LogText update AFTER updating FilteredLogLines
            ScheduleLogTextUpdate(); // This now also triggers UpdateSearchMatches

            if (wasReplace)
            {
                 _uiContext.Post(_ => { IsBusyFiltering = false; }, null);
            }
        }

        // Helper methods called by ApplyFilteredUpdate to modify UI collections.
        // These also trigger the Log Display Text Generation.
        private void AddFilteredLines(IReadOnlyList<FilteredLogLine> linesToAdd)
        {
            foreach (var line in linesToAdd)
            {
                FilteredLogLines.Add(line);
            }
            ScheduleLogTextUpdate(); // Trigger text update
        }

        private void ReplaceFilteredLines(IReadOnlyList<FilteredLogLine> newLines)
        {
            FilteredLogLines.Clear();
            foreach (var line in newLines)
            {
                FilteredLogLines.Add(line);
            }
            ScheduleLogTextUpdate(); // Trigger text update
        }

        // Central method to signal the processor that filters or context may have changed.
        private void TriggerFilterUpdate()
        {
            // Show busy indicator *before* starting the potentially long operation.
            IsBusyFiltering = true;

            // Post the actual filter update call to the dispatcher queue.
            // This allows the UI to update (show the spinner) before the potentially
            // blocking call to GetCurrentFilter or UpdateFilterSettings happens.
             _uiContext.Post(_ =>
             {
                 IFilter? currentFilter = GetCurrentFilter(); // Reads filter tree state
                 _logFilterProcessor.UpdateFilterSettings(currentFilter ?? new TrueFilter(), ContextLines); // Sends to processor
                 UpdateFilterSubstringsCommand.Execute(null); // Triggers Highlighting Configuration update
             }, null);
             // Alternative using Dispatcher:
             // Application.Current.Dispatcher.BeginInvoke(new Action(() =>
             //{
             //    var currentFilter = GetCurrentFilter(); // Reads filter tree state
             //    _logFilterProcessor.UpdateFilterSettings(currentFilter, ContextLines); // Sends to processor
             //    UpdateFilterSubstringsCommand.Execute(null); // Triggers Highlighting Configuration update
             //}));
        }

        // Handles errors reported by the processor's observable stream.
        private void HandleProcessorError(string contextMessage, Exception ex)
        {
            Debug.WriteLine($"{contextMessage}: {ex}");
            // TODO: Implement user-facing error reporting (e.g., status bar, dialog).
            // Example:
            // _uiContext.Post(_ => MessageBox.Show($"Error processing logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning), null);
        }

        #endregion // --- Orchestration with ILogFilterProcessor ---

        #region // --- Log Display Text Generation ---
        // Manages the asynchronous update of the LogText property for AvalonEdit.

        // Schedules the LogText update to run after current UI operations.
        private void ScheduleLogTextUpdate()
        {
            lock (_logTextUpdateLock)
            {
                if (!_logTextUpdateScheduled)
                {
                    _logTextUpdateScheduled = true;
                    // Interaction with UI Services (SynchronizationContext):
                    _uiContext.Post(_ =>
                    {
                        lock (_logTextUpdateLock)
                        {
                            _logTextUpdateScheduled = false;
                        }
                        UpdateLogTextInternal(); // Execute the update
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
            catch (InvalidOperationException ioex)
            {
                // Handle potential collection modified error (should be rare with Post)
                Debug.WriteLine($"Error during UpdateLogTextInternal (Collection modified?): {ioex}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Generic error during UpdateLogTextInternal: {ex}");
            }
        }

        // Core search logic
        private void UpdateSearchMatches()
        {
             string currentSearchTerm = SearchText; // Use local copy
             string textToSearch = LogText; // Use local copy

             // Clear previous results
             _searchMatches.Clear();
             SearchMarkers.Clear(); // Clear the collection for the ruler
             _currentSearchIndex = -1;
             // Clear selection in editor immediately
             SelectAndScrollToCurrentMatch();

             if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
             {
                 OnPropertyChanged(nameof(SearchStatusText)); // Update status (e.g., clear it)
                 return; // Nothing to search for or in
             }

             int offset = 0;
             while (offset < textToSearch.Length)
             {
                 // Use case sensitivity setting for search
                 int foundIndex = textToSearch.IndexOf(
                     currentSearchTerm, 
                     offset, 
                     IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

                 if (foundIndex == -1)
                 {
                     break; // No more matches
                 }

                 var newMatch = new SearchResult(foundIndex, currentSearchTerm.Length);
                 _searchMatches.Add(newMatch);
                 SearchMarkers.Add(newMatch); // Add to the collection for the ruler
                 offset = foundIndex + 1;
             }

             OnPropertyChanged(nameof(SearchStatusText)); // Update match count display
        }

        // Trigger search update when case sensitivity changes
        partial void OnIsCaseSensitiveSearchChanged(bool value)
        {
            UpdateSearchMatches();
        }

        /// <summary>
        /// Loads log content from a text string, similar to loading from a file
        /// </summary>
        /// <param name="text">The text content to load as log</param>
        public void LoadLogFromText(string text)
        {
            try
            {
                // Clear existing log content
                LogDoc.Clear();
                FilteredLogLines.Clear();
                LogText = string.Empty;
                LogDoc.AddInitialLines(text);

                // Trigger filter update to process the new content
                TriggerFilterUpdate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading log content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Update search index based on clicked position in text
        public void UpdateSearchIndexFromCharacterOffset(int characterOffset)
        {
            if (_searchMatches.Count == 0) return;

            // Find the closest match before or at the clicked position
            int newIndex = -1;
            int minDistance = int.MaxValue;

            for (int i = 0; i < _searchMatches.Count; i++)
            {
                var match = _searchMatches[i];
                int distance = Math.Abs(match.Offset - characterOffset);
                
                if (distance < minDistance)
                {
                    minDistance = distance;
                    newIndex = i;
                }
            }

            if (newIndex != -1 && newIndex != _currentSearchIndex)
            {
                _currentSearchIndex = newIndex;
                SelectAndScrollToCurrentMatch();
                OnPropertyChanged(nameof(SearchStatusText));
            }
        }

        #endregion // --- Log Display Text Generation ---

        #region // --- Highlighting Configuration ---
        // Generates the list of patterns for AvalonEdit highlighting based on filters.

        // Helper method to recursively traverse the filter tree UI representation.
        private void TraverseFilterTreeForHighlighting(FilterViewModel filterViewModel, ObservableCollection<string> patterns)
        {
            // Reads UI State (Filter Tree Enabled/Value)
            if (!filterViewModel.Enabled) return;

            string? pattern = null;
            bool isRegex = false;

            if (filterViewModel.FilterModel is SubstringFilter sf && !string.IsNullOrEmpty(sf.Value))
            {
                pattern = Regex.Escape(sf.Value); // Escape for regex highlighting
                isRegex = false;
            }
            else if (filterViewModel.FilterModel is RegexFilter rf && !string.IsNullOrEmpty(rf.Value))
            {
                pattern = rf.Value; // Use raw regex pattern
                isRegex = true;
            }

            if (pattern != null)
            {
                try
                {
                    // Basic validation before adding to the list
                    if (isRegex) { _ = new Regex(pattern); } // Throws if invalid
                    if (!patterns.Contains(pattern))
                        patterns.Add(pattern);
                }
                catch (ArgumentException ex)
                {
                    // Log invalid patterns found in the filter tree
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

        // Helper method to extract the current filter configuration (used by Orchestration).
        // Note: Reads filter tree state.
        private IFilter? GetCurrentFilter()
        {
            return FilterProfiles.FirstOrDefault()?.FilterModel;
        }

        #endregion // --- Highlighting Configuration ---

        #region // --- Lifecycle Management ---
        // Implements IDisposable and handles cleanup of resources like subscriptions.

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposables.Dispose(); // Disposes processor and subscriptions
            }
        }

        // Called explicitly from the Window's Closing event.
        public void Cleanup()
        {
            IsBusyFiltering = false; // Ensure busy indicator is hidden on exit
            SaveCurrentSettings();
            Dispose();
        }

        #endregion // --- Lifecycle Management ---

        #region // --- Context Lines Commands ---

        [RelayCommand(CanExecute = nameof(CanDecrementContextLines))]
        private void DecrementContextLines()
        {
            // Decrement but ensure it doesn't go below 0
            ContextLines = Math.Max(0, ContextLines - 1);
            // The OnContextLinesChanged partial method will automatically trigger the filter update.
        }

        private bool CanDecrementContextLines()
        {
            // Can only decrement if the value is greater than 0
            return ContextLines > 0;
        }

        [RelayCommand]
        private void IncrementContextLines()
        {
            // Increment the value
            ContextLines++;
            // The OnContextLinesChanged partial method will automatically trigger the filter update.
        }

        // Ensure the ContextLines property setter itself also guards against negative values
        // (although the command prevents it now, this is good practice)
        // Modify the existing [ObservableProperty] backing field access if needed,
        // or rely on the converter/command logic. The current setup with the converter
        // and the CanExecute on the command is sufficient.

        // Also, ensure the OnContextLinesChanged partial method correctly triggers updates:
        partial void OnContextLinesChanged(int value)
        {
            // Re-evaluate CanExecute for Decrement command whenever ContextLines changes
            DecrementContextLinesCommand.NotifyCanExecuteChanged();
            TriggerFilterUpdate();
        }


        #endregion // --- Context Lines Commands ---
    }
    /// <summary>
    /// Represents the position and length of a found search match within the text.
    /// </summary>
    public record SearchResult(int Offset, int Length);
}