using System; // Added for Environment.NewLine, InvalidOperationException
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq; // Added for Select, FirstOrDefault
using System.Reactive.Disposables; // For CompositeDisposable
using System.Text.RegularExpressions;
using System.Threading; // For SynchronizationContext
using System.Windows; // For Visibility, MessageBox
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;         // Added for ILogFilterProcessor, FilteredUpdate, UpdateType
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
        private string _searchText = "";

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

        // --- Search Commands (Placeholders) ---
        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void PreviousSearch() { Debug.WriteLine("Previous search triggered."); /* TODO: Implement Search Logic */ }
        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch() { Debug.WriteLine("Next search triggered."); /* TODO: Implement Search Logic */ }
        private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

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
                ReplaceFilteredLines(update.Lines); // Updates UI State
            }
            else // Append
            {
                AddFilteredLines(update.Lines); // Updates UI State
            }

            // If this update was the result of a full re-filter (Replace),
            // hide the busy indicator *after* the UI collections are updated.
            // We post this with low priority to ensure UI updates take precedence.
            if (wasReplace)
            {
                _uiContext.Post(_ =>
                {
                    IsBusyFiltering = false;
                }, null);
                // Alternative using Dispatcher if preferred (though _uiContext should work):
                // Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => IsBusy = false));
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
                 var currentFilter = GetCurrentFilter(); // Reads filter tree state
                 _logFilterProcessor.UpdateFilterSettings(currentFilter, ContextLines); // Sends to processor
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

        // Handles property changes that require triggering a filter update.
        partial void OnContextLinesChanged(int value)
        {
            TriggerFilterUpdate();
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
                // Reads UI State (FilteredLogLines) and updates UI State (LogText)
                var textOnly = FilteredLogLines.Select(line => line.Text).ToList();
                LogText = string.Join(Environment.NewLine, textOnly);
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
        private IFilter GetCurrentFilter()
        {
            return FilterProfiles.FirstOrDefault()?.FilterModel ?? new TrueFilter();
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
                // Dispose LogTailerManager only if VM truly owns it (singleton might be disposed elsewhere)
                // LogTailerManager.Instance.Dispose();
                Debug.WriteLine("MainViewModel Disposed.");
            }
        }

        // Called explicitly from the Window's Closing event.
        public void Cleanup()
        {
            IsBusyFiltering = false; // Ensure busy indicator is hidden on exit
            Dispose();
        }

        #endregion // --- Lifecycle Management ---
    }
}