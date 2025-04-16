using System;
using System.Collections.Generic; // For List
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Text; // Required for StringBuilder
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows; // For MessageBox, Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.LogTailing;
using Logonaut.UI.Services; // Assuming IInputPromptService will be added here

namespace Logonaut.UI.ViewModels
{
    // --- Placeholder/Example Interface and Service for Input Dialog ---
    // In a real app, implement this using a proper custom dialog window.
    public interface IInputPromptService
    {
        string? ShowInputDialog(string title, string prompt, string defaultValue = "");
    }

    public class InputPromptService : IInputPromptService
    {
        public string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            // Requires adding a reference to Microsoft.VisualBasic:
            // try
            // {
            //    return Microsoft.VisualBasic.Interaction.InputBox(prompt, title, defaultValue);
            // }
            // catch (Exception ex) // Handle potential errors if VB Interaction not available/allowed
            // {
            //    Debug.WriteLine($"Error showing InputBox: {ex.Message}");
            //    MessageBox.Show("Error displaying input prompt. Please implement a custom dialog.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            //    return null;
            // }

            // --- TEMPORARY PLACEHOLDER ---
            // Replace this with a real dialog implementation
            var result = MessageBox.Show($"Placeholder for Input Dialog:\n'{prompt}'\n\nClick OK to simulate entering '{defaultValue}_modified'.\nClick Cancel to simulate cancelling.", title, MessageBoxButton.OKCancel);
            return (result == MessageBoxResult.OK) ? defaultValue + "_modified" : null;
            // --- END TEMPORARY PLACEHOLDER ---
        }
    }
    // --- End Placeholder ---


    public partial class MainViewModel : ObservableObject, IDisposable
    {
        #region // --- Fields ---

        // --- UI Services & Context ---
        private readonly IFileDialogService _fileDialogService;
        private readonly ISettingsService _settingsService;
        private readonly ILogTailerService _logTailerService;
        private readonly IInputPromptService _inputPromptService; // For renaming
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

        public MainViewModel(
            ISettingsService settingsService,
            ILogTailerService logTailerService,
            IFileDialogService? fileDialogService = null,
            IInputPromptService? inputPromptService = null,
            ILogFilterProcessor? logFilterProcessor = null,
            SynchronizationContext? uiContext = null)
        {
            _settingsService = settingsService;
            _logTailerService = logTailerService;
            _fileDialogService = fileDialogService ?? new FileDialogService();
            _inputPromptService = inputPromptService ?? new InputPromptService(); // Use default/placeholder
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

            // --- Lifecycle Management ---
            _disposables.Add(_logFilterProcessor);
            _disposables.Add(filterSubscription);

            // --- Initial State Setup ---
            Theme = new ThemeViewModel(); // Part of UI State
            LoadPersistedSettings(); // Load profiles and settings

            // Initial filter trigger is handled by setting ActiveFilterProfile in LoadPersistedSettings
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

        // Controls whether timestamp highlighting rules are applied in AvalonEdit.
        [ObservableProperty]
        private bool _highlightTimestamps = true;

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
        [NotifyCanExecuteChangedFor(nameof(RenameProfileCommand))]
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
            SaveCurrentSettings(); // Save changes immediately
        }

        [RelayCommand(CanExecute = nameof(CanManageActiveProfile))]
        private void RenameProfile()
        {
            if (ActiveFilterProfile == null) return;

            string currentName = ActiveFilterProfile.Name;
            // Use IInputPromptService to get new name
            string? newName = _inputPromptService.ShowInputDialog("Rename Filter Profile", $"Enter new name for '{currentName}':", currentName);

            if (!string.IsNullOrWhiteSpace(newName) && newName != currentName)
            {
                // Check for name conflicts (case-insensitive)
                if (AvailableProfiles.Any(p => p != ActiveFilterProfile && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"A profile named '{newName}' already exists.", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                // Update the ViewModel property, which updates the Model property via its OnChanged handler
                ActiveFilterProfile.Name = newName;
                // Force ComboBox refresh if binding doesn't update automatically when DisplayMemberPath source changes
                // This typically requires replacing the item or using a more complex VM structure if inline update needed.
                // A simpler way is often to just rely on the user re-selecting if the ComboBox doesn't update visually.
                // Or, re-sort/refresh the AvailableProfiles collection if necessary.
                SaveCurrentSettings(); // Save changes
            }
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
                else
                    SelectedFilterNode.EndEditCommand.Execute(null); // EndEdit uses callback, which triggers save
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
            }
        }

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void PreviousSearch()
        {
            if (_searchMatches.Count == 0) return;
            _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count; // Wrap around
            SelectAndScrollToCurrentMatch();
            OnPropertyChanged(nameof(SearchStatusText));
        }

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch()
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
            }
            else
            {
                CurrentMatchOffset = -1;
                CurrentMatchLength = 0;
            }
        }

        partial void OnSearchTextChanged(string value) => UpdateSearchMatches(); // Trigger search update

        [RelayCommand]
        private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
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

        [RelayCommand]
        private void IncrementContextLines()
        {
            ContextLines++;
            // OnContextLinesChanged triggers TriggerFilterUpdate
        }

        partial void OnContextLinesChanged(int value)
        {
            DecrementContextLinesCommand.NotifyCanExecuteChanged();
            TriggerFilterUpdate(); // Trigger re-filter when context changes
        }

        #endregion // --- Command Handling ---

        #region // --- Orchestration & Updates ---

        // This method is automatically called by the CommunityToolkit.Mvvm generator
        // when the ActiveFilterProfile property's value changes.
        partial void OnActiveFilterProfileChanged(FilterProfileViewModel? oldValue, FilterProfileViewModel? newValue)
        {
            // When the active profile changes:
            // 1. Update the collection bound to the TreeView
            UpdateActiveTreeRootNodes(newValue);
            // 2. Clear the node selection within the TreeView
            SelectedFilterNode = null;
            // 3. Trigger a re-filter using the new profile's rules.
            TriggerFilterUpdate();
            // 4. Save the settings
            SaveCurrentSettings();
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
        private void TriggerFilterUpdate()
        {
            // Simple check to avoid queuing multiple simultaneous updates. More robust
            // handling might involve cancellation or ensuring only the latest runs.
            if (IsBusyFiltering)
            {
                Debug.WriteLine("TriggerFilterUpdate skipped: Already busy.");
                return;
            }
            IsBusyFiltering = true;

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

            if (wasReplace)
            {
                ReplaceFilteredLines(update.Lines); // Updates UI State (FilteredLogLines)
                // Search markers are cleared and updated after LogText changes
            }
            else // Append
            {
                AddFilteredLines(update.Lines); // Updates UI State (FilteredLogLines)
            }

            // Schedule the LogText update AFTER updating FilteredLogLines
            ScheduleLogTextUpdate(); // This now also triggers UpdateSearchMatches

            // Turn off busy indicator only after a full Replace operation completes
            if (wasReplace)
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

        partial void OnIsCaseSensitiveSearchChanged(bool value) => UpdateSearchMatches(); // Trigger search update

        public void LoadLogFromText(string text)
        {
            _logFilterProcessor.Reset(); // Reset processor
            LogDoc.Clear();              // Clear internal document storage
            FilteredLogLines.Clear();    // Clear UI collection
            LogText = string.Empty;      // Clear editor text via binding
            _searchMatches.Clear();      // Clear search state
            SearchMarkers.Clear();
            _currentSearchIndex = -1;

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