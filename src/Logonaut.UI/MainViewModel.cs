using System;
using System.Collections.Generic; // For List
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Reactive.Linq;
using System.ComponentModel; // For PropertyChangedEventArgs
using System.Windows; // For MessageBox, Visibility
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.LogTailing;
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
        [ObservableProperty]
        private ObservableCollection<SearchResult> _searchMarkers = new();

        // Fields to track the active profile and its subscription
        private FilterProfileViewModel? _observedActiveProfile;
        private IDisposable? _activeProfileNameSubscription;


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

            // Subscribe to TotalLinesProcessed >>>
            var totalLinesSubscription = _logFilterProcessor.TotalLinesProcessed
                // No need to ObserveOn UI context if processor ensures UI thread updates,
                // but safer to explicitly marshal here just in case.
                // .ObserveOn(_uiContext) // Optional: Use if processor might emit on background
                .Subscribe(
                    count => _uiContext.Post(_ => TotalLogLines = count, null), // Update property on UI thread
                    ex => HandleProcessorError("Total Lines Error", ex)
                );

            // --- Lifecycle Management ---
            _disposables.Add(_logFilterProcessor);
            _disposables.Add(filterSubscription);
            _disposables.Add(totalLinesSubscription);

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
            SaveCurrentSettings(); // Save settings when this property changes
        }

        // Controls whether timestamp highlighting rules are applied in AvalonEdit.
        [ObservableProperty]
        private bool _highlightTimestamps = true;

        partial void OnHighlightTimestampsChanged(bool value)
        {
            SaveCurrentSettings(); // Save settings when this property changes
        }

        // Collection of filter patterns (substrings/regex) for highlighting.
        // Note: This state is derived by traversing the *active* FilterProfile.
        [ObservableProperty]
        private ObservableCollection<string> _filterSubstrings = new();

        [ObservableProperty]
        private bool _isBusyFiltering = false;

        // Controls whether search is case sensitive
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SearchStatusText))]
        private bool _isCaseSensitiveSearch = false;


        /// <summary>
        /// Collection of all available filter profiles (VMs) for the ComboBox.
        /// </summary>
        public ObservableCollection<FilterProfileViewModel> AvailableProfiles { get; } = new();

        /// <summary>
        /// The currently selected filter profile VM in the ComboBox.
        /// Setting this property triggers updates to the TreeView and filtering.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
        [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))] // Enable adding nodes if profile selected
        [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))] // Depends on node selection *within* active tree
        [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))] // Depends on node selection *within* active tree
        private FilterProfileViewModel? _activeFilterProfile;

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
            ContextLines = settings.ContextLines;
            ShowLineNumbers = settings.ShowLineNumbers;
            HighlightTimestamps = settings.HighlightTimestamps;
            IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;
            // TODO: Apply theme based on settings.LastTheme

            // Rebuild the AvailableProfiles collection from the loaded filter profile models
            AvailableProfiles.Clear();
            FilterProfileViewModel? profileToSelect = null;
            foreach (var profileModel in settings.FilterProfiles)
            {
                // Pass the TriggerFilterUpdate method as the callback for changes *within* the profile tree
                var profileVM = new FilterProfileViewModel(profileModel, TriggerFilterUpdate);
                AvailableProfiles.Add(profileVM);
                // Identify the one to select based on the saved name
                if (profileModel.Name == settings.LastActiveProfileName)
                {
                    profileToSelect = profileVM;
                }
            }

            // Set the active profile. Should always find one due to validation in SettingsManager.
            // This assignment will trigger OnActiveFilterProfileChanged.
            ActiveFilterProfile = profileToSelect ?? AvailableProfiles.FirstOrDefault(); // Fallback just in case
        }

        private void SaveCurrentSettings()
        {
            // Ensure ActiveFilterProfile is up-to-date before saving its name
            string? activeProfileName = ActiveFilterProfile?.Name;

            var settingsToSave = new LogonautSettings
            {
                // Extract the models from the ViewModels
                FilterProfiles = AvailableProfiles.Select(vm => vm.Model).ToList(),
                LastActiveProfileName = activeProfileName, // Save the name of the active one
                // --- Other settings ---
                ContextLines = this.ContextLines,
                ShowLineNumbers = this.ShowLineNumbers,
                HighlightTimestamps = this.HighlightTimestamps,
                IsCaseSensitiveSearch = this.IsCaseSensitiveSearch,
                // TODO: Add theme, window state etc.
            };

            _settingsService.SaveSettings(settingsToSave);
        }

        #endregion // --- UI State Management ---

        #region // --- Command Handling ---

        // === Profile Management Commands ===
        [RelayCommand]
        private void CreateNewProfile()
        {
            int counter = 1;
            string baseName = "New Profile ";
            string newName = baseName + counter;
            // Ensure generated name is unique
            while (AvailableProfiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                counter++;
                newName = baseName + counter;
            }

            // Create the underlying model and its ViewModel wrapper
            var newProfileModel = new FilterProfile(newName, null); // Start with a simple root
            var newProfileVM = new FilterProfileViewModel(newProfileModel, TriggerFilterUpdate);

            AvailableProfiles.Add(newProfileVM);
            ActiveFilterProfile = newProfileVM; // Select the new profile (this triggers update via OnChanged)

            // Immediately trigger the rename mode for the new profile VM
            // Use Dispatcher.InvokeAsync to ensure UI updates (like setting ActiveFilterProfile)
            // have likely processed before trying to execute the command that relies on it being active.
            // Using BeginInvoke with Background priority can also work well here.
            _uiContext.Post(_ => { // Use the SynchronizationContext instead
                if (ActiveFilterProfile == newProfileVM) // Double-check it's still the active one
                {
                    newProfileVM.BeginRenameCommand.Execute(null);
                    // Focus should be handled automatically by TextBoxHelper.FocusOnVisible
                }
            }, null); // Pass null for the state object

            SaveCurrentSettings(); // Save changes immediately
        }

        [RelayCommand(CanExecute = nameof(CanManageActiveProfile))]
        private void DeleteProfile()
        {
            if (ActiveFilterProfile == null) return;

            // Keep the profile to remove and its index for selection logic later
            var profileToRemove = ActiveFilterProfile;
            int removedIndex = AvailableProfiles.IndexOf(profileToRemove);

            // --- Logic for Deletion ---
            AvailableProfiles.Remove(profileToRemove); // Remove the selected profile

            // --- Handle Last Profile Scenario ---
            if (AvailableProfiles.Count == 0)
            {
                // If the list is now empty, create and add a new default profile
                var defaultModel = new FilterProfile("Default", null); // Use default name and no filter
                var defaultVM = new FilterProfileViewModel(defaultModel, TriggerFilterUpdate);
                AvailableProfiles.Add(defaultVM);

                // Set the new default profile as the active one
                ActiveFilterProfile = defaultVM;
                // Setting ActiveFilterProfile triggers OnActiveFilterProfileChanged -> UpdateActiveTreeRootNodes & TriggerFilterUpdate
            }
            else
            {
                // Select another profile (e.g., the previous one or the first one)
                ActiveFilterProfile = AvailableProfiles.ElementAtOrDefault(Math.Max(0, removedIndex - 1)) ?? AvailableProfiles.First();
                // Setting ActiveFilterProfile triggers OnActiveFilterProfileChanged -> UpdateActiveTreeRootNodes & TriggerFilterUpdate
            }

            SaveCurrentSettings(); // Save the updated profile list and active profile
        }

        private bool CanManageActiveProfile() => ActiveFilterProfile != null;

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
             SaveCurrentSettings();
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
            SaveCurrentSettings();
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
                    SaveCurrentSettings();
                }
            }
        }
        private bool CanToggleEditNode() => SelectedFilterNode?.IsEditable ?? false;

        // === Other Commands (File, Search, Context Lines) ===
        // Ensure they interact correctly with the Active Profile concept if needed

        [RelayCommand]
        private void OpenLogFile()
        {
            string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
            if (string.IsNullOrEmpty(selectedFile)) return;

            _logFilterProcessor.Reset(); // Reset processor state (clears doc, etc.)
            FilteredLogLines.Clear();    // Clear UI collection immediately
            OnPropertyChanged(nameof(FilteredLogLinesCount)); // Notify count changed
            ScheduleLogTextUpdate();     // Update AvalonEdit to empty
            CurrentLogFilePath = selectedFile; // Update state

            try
            {
                _logTailerService.ChangeFile(selectedFile);

                TriggerFilterUpdate(); // TODO: Not sure this is needed.
            }
            catch (Exception ex)
            {
                IsBusyFiltering = false; // Ensure busy indicator off on error
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

        [RelayCommand] private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
        {
            var newFilterSubstrings = new ObservableCollection<string>();
            // Traverse the tree of the *currently active* profile
            if (ActiveFilterProfile?.RootFilterViewModel != null)
            {
                TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterSubstrings);
            }
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
            SaveCurrentSettings();
        }

        #endregion // --- Command Handling ---

        #region // --- Orchestration & Updates ---

        // This method is automatically called by the CommunityToolkit.Mvvm generator
        // when the ActiveFilterProfile property's value changes.
        partial void OnActiveFilterProfileChanged(FilterProfileViewModel? oldValue, FilterProfileViewModel? newValue)
        {
            // --- Unsubscribe from the old profile's name changes ---
            _activeProfileNameSubscription?.Dispose();
            _observedActiveProfile = null;

            if (newValue != null)
            {
                _observedActiveProfile = newValue;
                // --- Subscribe to the new profile's name changes ---
                // Explicitly specify the Delegate type AND EventArgs type
                _activeProfileNameSubscription = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                        handler => newValue.PropertyChanged += handler, // handler is now correctly typed
                        handler => newValue.PropertyChanged -= handler)
                    .Where(pattern => pattern.EventArgs.PropertyName == nameof(FilterProfileViewModel.Name)) // Check EventArgs property name
                    // Optional: Add debounce/throttle if changes trigger too rapidly
                    // .Throttle(TimeSpan.FromMilliseconds(200), _uiScheduler)
                    .ObserveOn(_uiContext) // Ensure handler runs on UI thread
                    .Subscribe(pattern => HandleActiveProfileNameChange(pattern.Sender as FilterProfileViewModel)); // pattern.Sender is the source

                // Keep existing logic
                UpdateActiveTreeRootNodes(newValue);
                SelectedFilterNode = null;
                TriggerFilterUpdate();
                // SaveCurrentSettings(); // Save triggered by name change or initial selection
            }
            else // No active profile
            {
                UpdateActiveTreeRootNodes(null);
                SelectedFilterNode = null;
                TriggerFilterUpdate(); // Trigger with default filter
            }
            // Save immediately on selection change as well
            SaveCurrentSettings();
        }


        private void HandleActiveProfileNameChange(FilterProfileViewModel? profileVM)
        {
            if (profileVM == null || profileVM != ActiveFilterProfile)
            {
                // Stale event or profile changed again quickly, ignore.
                return;
            }

            string newName = profileVM.Name;
            string modelName = profileVM.Model.Name; // Get the last known committed name

            // --- Validation ---
            // Check for empty/whitespace
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("Profile name cannot be empty.", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                profileVM.Name = modelName; // Revert VM property immediately
                // No save needed here, as the invalid name wasn't saved.
                return;
            }

            // Check for duplicates (case-insensitive, excluding self)
            if (AvailableProfiles.Any(p => p != profileVM && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"A profile named '{newName}' already exists.", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                profileVM.Name = modelName; // Revert VM property immediately
                // No save needed here.
                return;
            }

            // --- Validation Passed ---
            // Ensure the model is updated (might be redundant if binding worked, but safe)
            if (profileVM.Model.Name != newName)
            {
                profileVM.Model.Name = newName;
            }
            SaveCurrentSettings(); // Save the valid new name
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
            // Post the work to the UI thread's dispatcher queue. This allows the
            // IsBusyFiltering = true change to render before potentially blocking work.
            _uiContext.Post(_ =>
            {
                // Get the filter model from the *currently selected* profile VM
                IFilter? filterToApply = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();

                // Send the filter and context lines to the background processor
                _logFilterProcessor.UpdateFilterSettings(filterToApply, ContextLines);

                // Update highlighting rules based on the *active* filter tree
                UpdateFilterSubstringsCommand.Execute(null);
            }, null);
        }


        // Called by the processor subscription to apply incoming updates.
        private void ApplyFilteredUpdate(FilteredUpdate update)
        {
            bool wasReplace = update.Type == UpdateType.Replace;
            int originalLineToRestore = -1; // Store the original line number

            if (wasReplace)
            {
                IsBusyFiltering = true; // Indicate UI update is starting
                originalLineToRestore = HighlightedOriginalLineNumber; // Store before clearing
                ReplaceFilteredLines(update.Lines); // Updates UI State (FilteredLogLines)
                // Search markers are cleared and updated after LogText changes
            }
            else // Append
            {
                AddFilteredLines(update.Lines); // Updates UI State (FilteredLogLines)
                // Don't change IsBusyFiltering for Appends
            }

            // Schedule the LogText update AFTER updating FilteredLogLines
            ScheduleLogTextUpdate(); // This now also triggers UpdateSearchMatches

            // Turn off busy indicator only after a full Replace operation completes
            if (wasReplace)
            {
                // --- Restore Highlight After Replace ---
                if (originalLineToRestore > 0)
                {
                    // Find the new index of the line with the stored original number
                    int newIndex = FilteredLogLines
                        .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                        .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;

                    // Update the highlight index *after* the UI potentially updates from ReplaceFilteredLines
                    _uiContext.Post(_ => { HighlightedFilteredLineIndex = newIndex; }, null);
                }
                else
                {
                    // If no line was previously highlighted, ensure it's reset
                    _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
                }

                // Ensure this runs *after* the UI updates from ReplaceFilteredLines have likely settled.
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
            FilteredLogLines.Clear();
            _searchMatches.Clear(); // Clear internal match list on replace
            SearchMarkers.Clear(); // Clear ruler markers
            _currentSearchIndex = -1; // Reset search index
            SelectAndScrollToCurrentMatch(); // Clear editor selection

            foreach (var line in newLines)
            {
                FilteredLogLines.Add(line);
            }
            OnPropertyChanged(nameof(FilteredLogLinesCount));
            // ScheduleLogTextUpdate(); // Schedule is called by ApplyFilteredUpdate now
        }

        // Schedules the LogText update to run after current UI operations.
        private void ScheduleLogTextUpdate()
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
             // TODO: Some code here is duplicated with ReplaceFilteredLines. Consider refactoring.
             _searchMatches.Clear();
             SearchMarkers.Clear(); // Clear the collection for the ruler
             _currentSearchIndex = -1;
             SelectAndScrollToCurrentMatch(); // Clear selection in editor

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
            SaveCurrentSettings(); // <<< FIX: Save settings >>>
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