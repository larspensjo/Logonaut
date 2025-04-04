using System.Collections.ObjectModel;
using System.Diagnostics;
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
    // Using partial class for potential future organization (e.g., MainViewModel.Commands.cs)
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        public ThemeViewModel Theme { get; } = new();

        // LogDocument remains to hold the full log, managed by LogFilterProcessor
        public LogDocument LogDoc { get; } = new();

        // The filtered lines collection, updated by the LogFilterProcessor subscription
        public ObservableCollection<FilteredLogLine> FilteredLogLines { get; } = new();

        // Property for binding to AvalonEdit (holds only the text content)
        [ObservableProperty]
        private string _logText = string.Empty;

        // Search text property
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PreviousSearchCommand))]
        [NotifyCanExecuteChangedFor(nameof(NextSearchCommand))]
        private string _searchText = "";

        // Current file path property
        [ObservableProperty]
        private string? _currentLogFilePath;

        // Filter profiles (tree structure for UI)
        public ObservableCollection<FilterViewModel> FilterProfiles { get; } = new();

        // Selected filter in the tree view
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveFilterCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleEditCommand))]
        private FilterViewModel? _selectedFilter;

        // Context lines (TODO: Add UI control to change this)
        [ObservableProperty]
        private int _contextLines = 0; // Default to 0

        // UI Services
        private readonly IFileDialogService _fileDialogService;
        private readonly ILogFilterProcessor _logFilterProcessor; // <<< New Service

        // --- UI Update Scheduling ---
        private bool _logTextUpdateScheduled = false;
        private readonly object _logTextUpdateLock = new object();
        private readonly SynchronizationContext _uiContext;

        // --- Rx Disposables ---
        private readonly CompositeDisposable _disposables = new();

        public MainViewModel(IFileDialogService? fileDialogService = null, ILogFilterProcessor? logFilterProcessor = null)
        {
            _fileDialogService = fileDialogService ?? new FileDialogService();

            _uiContext = SynchronizationContext.Current ?? throw new InvalidOperationException("Could not capture SynchronizationContext. Ensure ViewModel is created on the UI thread.");

            // --- Initialize Log Filter Processor ---
            // The processor now takes the raw lines stream and the LogDocument
            _logFilterProcessor = logFilterProcessor ?? new LogFilterProcessor(
                LogTailerManager.Instance.LogLines,
                LogDoc, // Pass the LogDocument instance
                _uiContext);

            _disposables.Add(_logFilterProcessor);

            // --- Subscribe to Filtered Updates from the Processor ---
            var filterSubscription = _logFilterProcessor.FilteredUpdates
                // No ObserveOn needed here, processor ensures updates are on UI context
                .Subscribe(
                    update => ApplyFilteredUpdate(update),
                    ex => HandleProcessorError("Log Processing Error", ex) // Handle errors from the processor stream
                );
            _disposables.Add(filterSubscription);

            // Initial filter update
            TriggerFilterUpdate();
        }

        // --- Apply Updates from LogFilterProcessor ---
        private void ApplyFilteredUpdate(FilteredUpdate update)
        {
            if (update.Type == UpdateType.Replace)
            {
                ReplaceFilteredLines(update.Lines);
            }
            else // Append
            {
                AddFilteredLines(update.Lines);
            }
        }

        // --- UI Update Methods (Called by ApplyFilteredUpdate on UI Thread) ---
        private void AddFilteredLines(IReadOnlyList<FilteredLogLine> linesToAdd)
        {
            foreach (var line in linesToAdd)
            {
                FilteredLogLines.Add(line);
            }
            ScheduleLogTextUpdate();
        }

        private void ReplaceFilteredLines(IReadOnlyList<FilteredLogLine> newLines)
        {
            FilteredLogLines.Clear();
            foreach (var line in newLines)
            {
                FilteredLogLines.Add(line);
            }
            ScheduleLogTextUpdate();
        }

        // --- Schedule the UpdateLogText call (Remains the Same) ---
        private void ScheduleLogTextUpdate()
        {
            lock (_logTextUpdateLock)
            {
                if (!_logTextUpdateScheduled)
                {
                    _logTextUpdateScheduled = true;
                    _uiContext.Post(_ =>
                    {
                        lock (_logTextUpdateLock)
                        {
                            _logTextUpdateScheduled = false;
                        }
                        UpdateLogTextInternal();
                    }, null);
                }
            }
        }

        // --- Internal LogText Update (Remains the Same) ---
        private void UpdateLogTextInternal()
        {
            try
            {
                var textOnly = FilteredLogLines.Select(line => line.Text).ToList();
                LogText = string.Join(Environment.NewLine, textOnly);
            }
            catch (InvalidOperationException ioex)
            {
                Debug.WriteLine($"Error during UpdateLogTextInternal (Collection modified?): {ioex}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Generic error during UpdateLogTextInternal: {ex}");
            }
        }

        // --- Helper to get the current filter from the UI structure ---
        private IFilter GetCurrentFilter()
        {
            return FilterProfiles.FirstOrDefault()?.FilterModel ?? new TrueFilter();
        }

        // --- Trigger Filter Update in the Processor ---
        private void TriggerFilterUpdate()
        {
            var currentFilter = GetCurrentFilter();
            _logFilterProcessor.UpdateFilterSettings(currentFilter, ContextLines); // Pass context lines
            // Update highlighting based on the new filter state
            UpdateFilterSubstringsCommand.Execute(null);
        }

        // --- Error Handling ---
        private void HandleProcessorError(string contextMessage, Exception ex)
        {
            Debug.WriteLine($"{contextMessage}: {ex}");
            // Consider showing a user-friendly error message
            // _uiContext.Post(_ => MessageBox.Show($"Error processing logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning), null);
        }

        // --- Commands ---

        // Command methods now trigger TriggerFilterUpdate after modifying the FilterProfiles
        [RelayCommand]
        private void AddSubstringFilter() => AddFilter(new SubstringFilter(""));
        [RelayCommand]
        private void AddRegexFilter() => AddFilter(new RegexFilter(".*"));
        [RelayCommand]
        private void AddAndFilter() => AddFilter(new AndFilter());
        [RelayCommand]
        private void AddOrFilter() => AddFilter(new OrFilter());
        [RelayCommand]
        private void AddNorFilter() => AddFilter(new NorFilter());

        private void AddFilter(IFilter filter)
        {
            // Pass the TriggerFilterUpdate method as the callback
            var newFilterVM = new FilterViewModel(filter, TriggerFilterUpdate);

            if (FilterProfiles.Count == 0)
            {
                FilterProfiles.Add(newFilterVM);
                SelectedFilter = newFilterVM; // Auto-select the new root
            }
            else if (SelectedFilter != null && SelectedFilter.FilterModel is CompositeFilter)
            {
                SelectedFilter.AddChildFilter(filter); // AddChildFilter uses the callback
            }
            else
            {
                MessageBox.Show(
                    SelectedFilter == null
                    ? "Please select a composite filter node (And, Or, Nor) first to add a child."
                    : "Selected filter is not a composite filter (And, Or, Nor). Cannot add a child filter here.",
                    "Add Filter Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return; // Don't trigger update if nothing was added
            }
            // Trigger update AFTER adding the filter to the structure
            TriggerFilterUpdate();
        }

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
                parent.RemoveChild(SelectedFilter); // RemoveChild uses callback
                removed = true;
                SelectedFilter = parent; // Select parent after removing child
            }

            if (removed)
            {
                // Trigger update AFTER removing the filter
                TriggerFilterUpdate();
            }
        }
        private bool CanRemoveFilter() => SelectedFilter != null;


        // ToggleEdit now implicitly triggers update via EndEdit -> Callback -> TriggerFilterUpdate
        [RelayCommand(CanExecute = nameof(CanToggleEdit))]
        private void ToggleEdit()
        {
            if (SelectedFilter?.IsEditable ?? false)
            {
                if (SelectedFilter.IsNotEditing)
                    SelectedFilter.BeginEdit();
                else
                    SelectedFilter.EndEdit(); // EndEdit calls the callback
            }
        }
        private bool CanToggleEdit() => SelectedFilter?.IsEditable ?? false;

        // Handling selection change (remains the same)
        partial void OnSelectedFilterChanged(FilterViewModel? oldValue, FilterViewModel? newValue)
        {
            if (oldValue != null && oldValue.IsEditing)
            {
                oldValue.EndEditCommand.Execute(null);
            }
            if (oldValue != null) oldValue.IsSelected = false;
            if (newValue != null) newValue.IsSelected = true;
        }

        // ContextLines change should trigger a re-filter
        partial void OnContextLinesChanged(int value)
        {
            TriggerFilterUpdate();
        }

        // --- Search Commands (Remain the Same) ---
        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void PreviousSearch() { Debug.WriteLine("Previous search triggered."); /* TODO */ }
        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch() { Debug.WriteLine("Next search triggered."); /* TODO */ }
        private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

        // --- File Handling ---
        [RelayCommand]
        private void OpenLogFile()
        {
            string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
            if (string.IsNullOrEmpty(selectedFile))
                return;

            // Reset processor state BEFORE changing the tailer
            _logFilterProcessor.Reset();
            // Clear UI collections immediately
            FilteredLogLines.Clear();
            ScheduleLogTextUpdate(); // Update LogText to empty

            CurrentLogFilePath = selectedFile;

            try
            {
                // Change the underlying tailer; the processor is already listening
                LogTailerManager.Instance.ChangeFile(selectedFile);
                // No need to explicitly restart processor; Reset + new lines handle it.
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening or monitoring log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentLogFilePath = null; // Reset path on error
                _logFilterProcessor.Reset(); // Also reset processor on error
            }
        }

        // --- UI Properties (ShowLineNumbers, HighlightTimestamps remain) ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomLineNumberMarginVisible))]
        private bool _showLineNumbers = true;
        public Visibility IsCustomLineNumberMarginVisible => ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

        [ObservableProperty]
        private bool _highlightTimestamps = true; // This controls AvalonEdit highlighting rule application

        // --- Filter Highlighting (Logic remains in ViewModel as it reads UI Filter Tree) ---
        [ObservableProperty]
        private ObservableCollection<string> _filterSubstrings = new();

        [RelayCommand]
        private void UpdateFilterSubstrings()
        {
            var newFilterSubstrings = new ObservableCollection<string>();
            if (FilterProfiles.Count > 0)
            {
                TraverseFilterTreeForHighlighting(FilterProfiles[0], newFilterSubstrings);
            }
            FilterSubstrings = newFilterSubstrings; // Update the property bound to AvalonEditHelper
        }

        // Traverse logic remains the same
        private void TraverseFilterTreeForHighlighting(FilterViewModel filterViewModel, ObservableCollection<string> patterns)
        {
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
                    // Basic validation: Ensure regex patterns are valid before adding
                    if (isRegex) { _ = new Regex(pattern); }
                    if (!patterns.Contains(pattern))
                        patterns.Add(pattern);
                }
                catch (ArgumentException ex)
                {
                    Debug.WriteLine($"Invalid regex pattern skipped for highlighting: '{pattern}'. Error: {ex.Message}");
                    // Optionally: Provide UI feedback about the invalid regex in the filter
                }
            }

            foreach (var childFilter in filterViewModel.Children)
            {
                TraverseFilterTreeForHighlighting(childFilter, patterns);
            }
        }

        // --- IDisposable Implementation ---
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
                LogTailerManager.Instance.Dispose(); // Dispose the singleton manager if appropriate here
                Debug.WriteLine("MainViewModel Disposed.");
            }
        }

        public void Cleanup()
        {
            Dispose();
        }
    }
}