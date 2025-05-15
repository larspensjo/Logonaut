using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reflection;
using System.IO; // For Stream
using System.Windows; // For Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Services;
using Logonaut.UI.Commands;
using ICSharpCode.AvalonEdit;

namespace Logonaut.UI.ViewModels;

/*
 * Implements the primary ViewModel for Logonaut's main window.
 *
 * Purpose:
 * Orchestrates the application's core functionality, acting as the central hub
 * connecting services (settings, log sources, file dialogs) with the UI (MainWindow).
 * It manages application state and exposes data and commands for data binding.
 *
 * Role & Responsibilities:
 * - Owns and manages the overall application state (current log file, filter profiles,
 *   UI settings like line numbers/timestamps, search state, busy indicators).
 * - Coordinates the log processing pipeline by interacting with ILogSource and ILogFilterProcessor.
 * - Holds the canonical LogDocument and the derived FilteredLogLines collection for UI display.
 * - Manages the active filter profile and its associated filter tree ViewModel.
 * - Exposes commands for user actions (opening files, managing filters, searching, etc.).
 * - Loads and saves application settings via ISettingsService.
 * - Mediates communication between the View (MainWindow) and the Core/Service layers,
 *   adhering to the MVVM pattern.
 *
 * Benefits:
 * - Centralizes application logic and state management.
 * - Decouples the View (MainWindow) from the Core services and models.
 * - Enhances testability by allowing the UI logic to be tested independently.
 *
 * It utilizes the CommunityToolkit.Mvvm for observable properties and commands and manages
 * its lifecycle and resources via IDisposable.
 */
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    #region Fields

    // --- Services & Context ---
    private string? _lastOpenedFolderPath;
    private readonly IScheduler? _backgroundScheduler; // Make it possible to inject your own background scheduler
    private readonly ILogSourceProvider _sourceProvider;
    public static readonly object LoadingToken = new();
    public static readonly object FilteringToken = new();
    public static readonly object BurstToken = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    [ObservableProperty] private ILogSource _currentActiveLogSource;
    [ObservableProperty] private string? _selectedLogTextForFilter; // Will be set from MainWindow.xaml.cs

    private readonly SynchronizationContext _uiContext;
    private IReactiveFilteredLogStream _reactiveFilteredLogStream; // This will be recreated when switching sources

    private readonly HashSet<int> _existingOriginalLineNumbers = new HashSet<int>(); // A helper HashSet to efficiently track existing original line numbers
    private ILogSource? _fileLogSource; // Keep a reference to the file source instance

    // --- Lifecycle Management ---
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _filterSubscription;
    private IDisposable? _totalLinesSubscription;

    [ObservableProperty] private bool _isAutoScrollEnabled = true;
    public event EventHandler? RequestScrollToEnd; // Triggered when Auto Scroll is enabled
    public event EventHandler<int>? RequestScrollToLineIndex; // Event passes the 0-based index to scroll to

    public ObservableCollection<PaletteItemDescriptor> FilterPaletteItems { get; } = new();
    public PaletteItemDescriptor? InitializedSubstringPaletteItem { get; private set; }

    #endregion // --- Fields ---

    #region Stats Properties

    [ObservableProperty] private long _totalLogLines;
    public int FilteredLogLinesCount => FilteredLogLines.Count;

    #endregion // --- Stats Properties ---

    #region Constructor

    public MainViewModel(
        ISettingsService settingsService,
        ILogSourceProvider sourceProvider,
        IFileDialogService? fileDialogService = null,
        SynchronizationContext? uiContext = null,
        IScheduler? backgroundScheduler = null)
    {
        _settingsService = settingsService;
        _sourceProvider = sourceProvider;
        _fileDialogService = fileDialogService ?? new FileDialogService();
        _uiContext = uiContext ?? SynchronizationContext.Current ?? throw new InvalidOperationException("Could not capture or receive a valid SynchronizationContext.");
        _backgroundScheduler = backgroundScheduler;

        // Initialize with FileLogSource as the default
        _fileLogSource = _sourceProvider.CreateFileLogSource();
        CurrentActiveLogSource = _fileLogSource; // Start with file source active
        _disposables.Add(_fileLogSource); // Add file source to disposables

        // Create the initial processor with the file source
        _reactiveFilteredLogStream = CreateFilteredStream(CurrentActiveLogSource);
        _disposables.Add(_reactiveFilteredLogStream); // Add processor to disposables
        SubscribeToFilteredStream(); // Subscribe to the initial processor

        UndoCommand = new RelayCommand(Undo, CanUndo);
        RedoCommand = new RelayCommand(Redo, CanRedo);

        CurrentBusyStates.CollectionChanged += CurrentBusyStates_CollectionChanged;
        _disposables.Add(Disposable.Create(() =>
        {
            CurrentBusyStates.CollectionChanged -= CurrentBusyStates_CollectionChanged;
        }));

        Theme = new ThemeViewModel();
        LoadPersistedSettings(); // Loads basic settings, not files yet

        PopulateFilterPalette();

        ToggleAboutOverlayCommand = new RelayCommand(ExecuteToggleAboutOverlay);
        LoadRevisionHistory();
    }

    #region About command

    [ObservableProperty] private bool _isAboutOverlayVisible;
    [ObservableProperty] private string? _aboutRevisionHistory;
    public string ApplicationVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"; // Shows Major.Minor.Build
    public IRelayCommand ToggleAboutOverlayCommand { get; }

    private void ExecuteToggleAboutOverlay()
    {
        IsAboutOverlayVisible = !IsAboutOverlayVisible;
        if (IsAboutOverlayVisible && string.IsNullOrEmpty(AboutRevisionHistory))
        {
            // Should have been loaded by constructor, but as a fallback:
            LoadRevisionHistory();
        }
    }

    private void LoadRevisionHistory()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // IMPORTANT: Adjust the resource name if your default namespace or folder structure differs.
            // It's typically: YourDefaultNamespace.FolderPath.FileName.extension
            // For Logonaut.UI project, if RevisionHistory.txt is in the root, it would be "Logonaut.UI.RevisionHistory.txt"
            // If it's in a "Resources" subfolder, it might be "Logonaut.UI.Resources.RevisionHistory.txt"
            string resourceName = "Logonaut.UI.RevisionHistory.txt";

            // You can list all resource names to find the correct one during debugging:
            // string[] names = assembly.GetManifestResourceNames();
            // Debug.WriteLine("Available resources: " + string.Join(", ", names));

            using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    AboutRevisionHistory = "Error: Revision history resource not found.";
                    Debug.WriteLine($"Error: Could not find embedded resource '{resourceName}'");
                    return;
                }
                using (StreamReader reader = new StreamReader(stream))
                {
                    AboutRevisionHistory = reader.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            AboutRevisionHistory = $"Error loading revision history: {ex.Message}";
            Debug.WriteLine($"Exception loading revision history: {ex}");
        }
    }

    #endregion // About command

    private void PopulateFilterPalette()
    {
        InitializedSubstringPaletteItem = new PaletteItemDescriptor("<Selection>", "SubstringType", isDynamic: true); // "SubstringType" is key
        FilterPaletteItems.Add(InitializedSubstringPaletteItem);
        FilterPaletteItems.Add(new PaletteItemDescriptor("Substring: \"\"", "SubstringType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("Regex", "RegexType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("AND Group", "AndType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("OR Group", "OrType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("NOR Group", "NorType"));
    }

    const int MaxPaletteDisplayTextLength = 20; // Or whatever fits your UI
    partial void OnSelectedLogTextForFilterChanged(string? oldValue, string? newValue)
    {
        if (InitializedSubstringPaletteItem is null)
            throw new InvalidOperationException("InitializedSubstringPaletteItem is not initialized.");

        if (string.IsNullOrEmpty(newValue))
        {
            InitializedSubstringPaletteItem.IsEnabled = false;
            InitializedSubstringPaletteItem.DisplayName = "<Selection>"; // Reset display name
            InitializedSubstringPaletteItem.InitialValue = null;
        }
        else
        {
            // Ignore multi-line (Mainwindow.xaml.cs should pre-filter this)
            InitializedSubstringPaletteItem.InitialValue = newValue;
            string displayText = newValue;
            if (displayText.Length > MaxPaletteDisplayTextLength)
                displayText = displayText.Substring(0, MaxPaletteDisplayTextLength - 3) + "..."; // TODO: Use a more advanced one with '...' in the middle
            InitializedSubstringPaletteItem.DisplayName = $"Substring: \"{displayText}\"";
            InitializedSubstringPaletteItem.IsEnabled = true;
        }
    }

    private IReactiveFilteredLogStream CreateFilteredStream(ILogSource source)
    {
        Debug.WriteLine($"---> Creating ReactiveFilteredLogStream with source: {source.GetType().Name}");
        return new ReactiveFilteredLogStream(
            source,
            LogDoc,
            _uiContext,
            AddLineToLogDocument,
            _backgroundScheduler);
    }

    private void SubscribeToFilteredStream()
    {
        // Dispose previous subscriptions if they exist
        _filterSubscription?.Dispose();
        _totalLinesSubscription?.Dispose();

        Debug.WriteLine($"---> Subscribing to processor: {_reactiveFilteredLogStream.GetType().Name}");

        _filterSubscription = _reactiveFilteredLogStream.FilteredUpdates
            .ObserveOn(_uiContext) // Ensure UI thread for updates
            .Subscribe(
                update => ApplyFilteredUpdate(update),
                ex => HandleProcessorError("Log Processing Error", ex)
            );

        var samplingScheduler = Scheduler.Default;
        _totalLinesSubscription = _reactiveFilteredLogStream.TotalLinesProcessed
            .Sample(TimeSpan.FromMilliseconds(200), samplingScheduler)
            .ObserveOn(_uiContext) // Ensure UI thread for updates
            .Subscribe(
                count => ProcessTotalLinesUpdate(count),
                ex => HandleProcessorError("Total Lines Error", ex)
            );

        _disposables.Add(_filterSubscription);
        _disposables.Add(_totalLinesSubscription);
    }

    // Dispose processor and its subscriptions
    private void DisposeAndClearFilteredStream()
    {
        Debug.WriteLine($"---> Disposing processor and subscriptions.");
        _filterSubscription?.Dispose();
        _totalLinesSubscription?.Dispose();
        _filterSubscription = null;
        _totalLinesSubscription = null;

        // Assuming processor was added to _disposables when created
        if (_reactiveFilteredLogStream != null)
        {
            _disposables.Remove(_reactiveFilteredLogStream); // Remove from main collection before disposing
            _reactiveFilteredLogStream.Dispose();
        }
    }

    private void CurrentBusyStates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Whenever the collection changes (add, remove, reset),
        // notify the UI that the IsLoading property *might* have changed.
        // The binding system will then re-query the IsLoading getter.
        OnPropertyChanged(nameof(IsLoading));
    }

    #endregion // --- Constructor ---

    #region Highlighted Line State

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

            // 1. Set the highlighted index (for the visual highlight)
            HighlightedFilteredLineIndex = foundLine.Index;

            // 2. Explicitly request the View to scroll to this index
            RequestScrollToLineIndex?.Invoke(this, foundLine.Index); // <<< ADD THIS
        }
        else
        {
            // Line exists in original log but not in filtered view
            // OR line number is beyond the original log's total lines
            // (We can't easily distinguish without checking LogDoc count, which might be large)
            JumpStatusMessage = $"Line {targetLineNumber} not found in filtered view.";
            await TriggerInvalidInputFeedback();
            // No need to change HighlightedFilteredLineIndex if not found
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

    #region UI State Management

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

    // Responsible for reacting to the switch of CurrentActiveLogSource
    partial void OnCurrentActiveLogSourceChanged(ILogSource? oldValue, ILogSource newValue)
    {
        Debug.WriteLine($"---> CurrentActiveLogSource changed from {oldValue?.GetType().Name ?? "null"} to {newValue.GetType().Name}");


        // 1. Update CanExecute for GenerateBurstCommand
        GenerateBurstCommand.NotifyCanExecuteChanged();
    }

    public ThemeViewModel Theme { get; }

    // Central store for all original log lines, passed to processor but owned here.
    // The content is managed by current class.
    public LogDocument LogDoc { get; } = new();

    // Collection of filtered lines currently displayed in the UI.
    public ObservableCollection<FilteredLogLine> FilteredLogLines { get; } = new();

    // While not pure MVVM, the simplest way for this specific performance optimization is to allow the ViewModel to interact directly (but safely via the dispatcher)
    // with the TextEditor's document. This isn't always available, for example when running unit tests.
    private TextEditor? _logEditorInstance;
    public void SetLogEditorInstance(TextEditor editor)
    {
        _logEditorInstance = editor;
        if (FilteredLogLines.Any())
        {
            ScheduleLogTextUpdate(FilteredLogLines);
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
    public bool IsLoading => CurrentBusyStates.Contains(LoadingToken) || CurrentBusyStates.Contains(BurstToken);

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
        LogonautSettings settings = _settingsService.LoadSettings();

        // --- Load Filter Profiles FIRST ---
        // This ensures ActiveFilterProfile is set before any saves are triggered.
        LoadFilterProfiles(settings);

        // --- Load Display/Search Settings ---
        // Setting these might trigger saves, which is now safe.
        ShowLineNumbers = settings.ShowLineNumbers;
        HighlightTimestamps = settings.HighlightTimestamps;
        IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;
        ContextLines = settings.ContextLines;
        IsAutoScrollEnabled = settings.AutoScrollToTail;
        _lastOpenedFolderPath = settings.LastOpenedFolderPath;

        // --- Load Simulator Settings ---
        // Setting these might trigger saves, which is now safe.
        LoadSimulatorPersistedSettings(settings);
    }

    private void SaveCurrentSettingsDelayed() => _uiContext.Post(_ => SaveCurrentSettings(), null);

    private void SaveCurrentSettings()
    {
        var settingsToSave = new LogonautSettings
        {
            // --- Save Display/Search Settings ---
            ContextLines = this.ContextLines,
            ShowLineNumbers = this.ShowLineNumbers,
            HighlightTimestamps = this.HighlightTimestamps,
            IsCaseSensitiveSearch = this.IsCaseSensitiveSearch,
            AutoScrollToTail = this.IsAutoScrollEnabled,
            LastOpenedFolderPath = this._lastOpenedFolderPath,
        };

        SaveSimulatorSettings(settingsToSave);

        // --- Save Filter Profiles ---
        SaveFilterProfiles(settingsToSave); // Handles LastActiveProfileName and the list

        // --- Save to Service ---
        _settingsService.SaveSettings(settingsToSave);
    }
    #endregion // --- UI State Management ---

    #region Command Handling

    // === Filter Node Manipulation Commands (Operate on Active Profile's Tree) ===

    // Combined Add Filter command - type determined by parameter
    // TODO: This will be replaced by the DnD functionality in the future.
    [RelayCommand(CanExecute = nameof(CanAddFilterNode))]
    private void AddFilter(object? filterTypeParam) // Parameter likely string like "Substring", "And", etc.
    {
        if (ActiveFilterProfile == null) throw new InvalidOperationException("No active profile");

        IFilter newFilterNodeModel = CreateFilterModelFromType(filterTypeParam as string ?? string.Empty);
        FilterViewModel? targetParentVM = null;
        int? targetIndex = null; // Use null to add at the end by default

        // Case 1: Active profile's tree is currently empty
        if (ActiveFilterProfile.RootFilterViewModel == null)
        {
            // Cannot add to null root via AddFilterAction directly.
            // We need to set the root first, which isn't easily undoable in the current structure.
            // Let's handle setting the root OUTSIDE the undo system for now, or create a dedicated SetRootFilterAction.
            // Simple approach: Set root directly, bypass Undo for this specific case.
            ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel);
            UpdateActiveTreeRootNodes(ActiveFilterProfile); // Update TreeView source
            SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel; // Select the new root
            TriggerFilterUpdate(); // Explicitly trigger updates since ExecuteAction wasn't called
            SaveCurrentSettingsDelayed();
            Debug.WriteLine("AddFilter: Set new root node (outside Undo system).");
            if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
                SelectedFilterNode.BeginEditCommand.Execute(null);
            return;
        }
        // Case 2: A composite node is selected - add as child
        else if (SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter)
        {
            targetParentVM = SelectedFilterNode;
            targetIndex = targetParentVM.Children.Count; // Add at end
        }
        // Case 3: No node selected (but tree exists), or non-composite selected - Add to the root if it's composite
        else if (ActiveFilterProfile.RootFilterViewModel != null && ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter)
        {
            targetParentVM = ActiveFilterProfile.RootFilterViewModel;
            targetIndex = targetParentVM.Children.Count; // Add at end of root
            Debug.WriteLine("AddFilter: No selection or non-composite selected, adding to root.");
        }
        else // Root exists but isn't composite, and nothing valid is selected
        {
            Debug.WriteLine("AddFilter: Cannot add filter. Select a composite node or ensure root is composite.");
            // Optionally show a message to the user
            // MessageBox.Show("Please select a composite filter node (AND, OR, NOR) to add a child to.", "Add Filter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (targetParentVM == null)
            throw new InvalidOperationException("Failed to determine a valid parent for the new filter node.");

        var action = new AddFilterAction(targetParentVM, newFilterNodeModel, targetIndex);
        Execute(action); // Use the ICommandExecutor method

        // Try to select the newly added node AFTER execution
        // AddFilterAction's Execute should have added the VM
        var addedVM = targetParentVM.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);
        if (addedVM is null)
            throw new InvalidOperationException("Failed to find the newly added VM in the parent's children.");

        targetParentVM.IsExpanded = true;
        SelectedFilterNode = addedVM; // Update selection

        // Editing logic: If the new node is editable, start editing
        if (SelectedFilterNode.IsEditable)
        {
            // Execute BeginEdit directly on the selected VM
            SelectedFilterNode.BeginEditCommand.Execute(null);
        }
    }

    // Central method to handle adding a new filter node, typically initiated by a Drag-and-Drop operation.
    // This method determines the correct placement (root or child) and uses the Undo/Redo system.
    public void ExecuteAddFilterFromDrop(string filterTypeIdentifier, FilterViewModel? targetParentInDrop, int? dropIndexInTarget, string? initialValue = null)
    {
        IFilter newFilterNodeModel = CreateFilterModelFromType(filterTypeIdentifier, initialValue);

        FilterViewModel? actualTargetParentVM = targetParentInDrop;
        int? actualDropIndex = dropIndexInTarget;

        if (ActiveFilterProfile == null) throw new InvalidOperationException("No active profile for drop.");

        if (actualTargetParentVM == null) // Dropped on empty space in TreeView, not on a specific item
        {
            if (ActiveFilterProfile.RootFilterViewModel == null) // Case 1: Tree is empty, set as root
            {
                ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel); // This updates the Model
                ActiveFilterProfile.RefreshRootViewModel(); // This creates the VM for the new root
                UpdateActiveTreeRootNodes(ActiveFilterProfile); // This updates the collection bound to the TreeView
                SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel;
                // No "Action" executed for undo stack in this specific case (matches old button logic for setting initial root)
                TriggerFilterUpdate();
                SaveCurrentSettingsDelayed();
                Debug.WriteLine("ExecuteAddFilterFromDrop: Set new root node (outside Undo system).");
                if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
                {
                    SelectedFilterNode.BeginEditCommand.Execute(null);
                }
                return; // Action complete for setting root
            }
            else if (ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter) // Case 2: Tree not empty, root is composite, add to root
            {
                actualTargetParentVM = ActiveFilterProfile.RootFilterViewModel;
                actualDropIndex = actualTargetParentVM.Children.Count; // Add to end of root
            }
            else // Case 3: Tree not empty, root is not composite - invalid drop for adding to root
            {
                Debug.WriteLine("ExecuteAddFilterFromDrop: Cannot add. Root exists but is not composite, and drop was not on a composite item.");
                return;
            }
        }
        // If actualTargetParentVM was provided from drop, it should have already been validated as composite by the drop handler.

        if (actualTargetParentVM == null || !(actualTargetParentVM.Filter is CompositeFilter))
        {
            Debug.WriteLine("ExecuteAddFilterFromDrop: No valid composite parent found for adding the filter.");
            return;
        }

        int finalIndex = actualDropIndex ?? actualTargetParentVM.Children.Count;

        var action = new AddFilterAction(actualTargetParentVM, newFilterNodeModel, finalIndex);
        Execute(action); // Use the ICommandExecutor method (adds to undo stack, triggers save/update)

        // Try to find the added VM more robustly
        var addedVM = actualTargetParentVM.Children.FirstOrDefault(vm => vm.Filter == newFilterNodeModel);
        if (addedVM == null && finalIndex < actualTargetParentVM.Children.Count) // Check specific index if not last
        {
            addedVM = actualTargetParentVM.Children[finalIndex];
            if (addedVM.Filter != newFilterNodeModel) addedVM = null; // double check it's the one we added
        }
        // Fallback to LastOrDefault if specific index check failed or it was added at the end
        if (addedVM == null) addedVM = actualTargetParentVM.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);


        if (addedVM != null)
        {
            actualTargetParentVM.IsExpanded = true;
            SelectedFilterNode = addedVM;

            if (SelectedFilterNode.IsEditable)
            {
                SelectedFilterNode.BeginEditCommand.Execute(null);
            }
        }
        else
        {
            throw new InvalidOperationException("Failed to find the newly added VM in the parent's children.");
            // This might happen if the filter type already existed and was somehow merged, or an error in AddFilterAction.
            // For now, just log. If it becomes a persistent issue, further investigation into AddFilterAction's interaction with collections is needed.
        }
    }

    // Helper method to create an IFilter model instance from a type identifier string.
    private IFilter CreateFilterModelFromType(string typeIdentifier, string? initialValue = null)
    {
        return typeIdentifier switch
        {
            "SubstringType" => new SubstringFilter(initialValue ?? ""),
            "RegexType" => new RegexFilter(initialValue ?? ".*"),
            "AndType" => new AndFilter(),
            "OrType" => new OrFilter(),
            "NorType" => new NorFilter(),
            // "TRUE" filter type isn't typically added from a palette.
            _ => throw new ArgumentException($"Unknown filter type identifier: {typeIdentifier} in CreateFilterModelFromType"),
        };
    }

    private bool CanAddFilterNode()
    {
        if (ActiveFilterProfile == null) return false;
        bool isTreeEmpty = ActiveFilterProfile.RootFilterViewModel == null;
        bool isCompositeNodeSelected = SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter;
        // Allow adding if tree is empty OR composite selected OR root is composite (allows adding to root when nothing/leaf selected)
        bool isRootComposite = ActiveFilterProfile.RootFilterViewModel?.Filter is CompositeFilter;
        return isTreeEmpty || isCompositeNodeSelected || (ActiveFilterProfile.RootFilterViewModel != null && isRootComposite);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveFilterNode))]
    private void RemoveFilterNode()
    {
        if (SelectedFilterNode == null || ActiveFilterProfile?.RootFilterViewModel == null) return;

        FilterViewModel nodeToRemove = SelectedFilterNode;
        FilterViewModel? parent = nodeToRemove.Parent;

        // Case 1: Removing the root node
        if (nodeToRemove == ActiveFilterProfile.RootFilterViewModel)
        {
            // This action is hard to undo cleanly with the current structure.
            // Bypass Undo system for root removal for now.
            ActiveFilterProfile.SetModelRootFilter(null);
            UpdateActiveTreeRootNodes(ActiveFilterProfile);
            SelectedFilterNode = null;
            TriggerFilterUpdate();
            SaveCurrentSettingsDelayed();
            Debug.WriteLine("RemoveFilterNode: Removed root node (outside Undo system).");
        }
        // Case 2: Removing a child node
        else if (parent != null)
        {
            var action = new RemoveFilterAction(parent, nodeToRemove);
            Execute(action); // Use the ICommandExecutor method
            SelectedFilterNode = parent; // Select the parent after removal
        }
    }
    private bool CanRemoveFilterNode() => SelectedFilterNode != null && ActiveFilterProfile != null;

    [RelayCommand(CanExecute = nameof(CanToggleEditNode))]
    private void ToggleEditNode()
    {
        if (SelectedFilterNode?.IsEditable ?? false)
        {
            if (SelectedFilterNode.IsNotEditing)
            {
                SelectedFilterNode.BeginEditCommand.Execute(null);
            }
            else
            {
                SelectedFilterNode.EndEditCommand.Execute(null);
                // ExecuteAction called within EndEdit will handle save/update
            }
        }
    }

    private bool CanToggleEditNode() => SelectedFilterNode?.IsEditable ?? false;

    // Helper to reset document and related UI state.
    // IMPORTANT: Do not use a context.Post() method here, as the clearing operations and state changes must be done immediately.
    private void ResetLogDocumentAndUIStateImmediate()
    {
        // Reset Core State
        _reactiveFilteredLogStream.Reset(); // Resets processor's internal index and total lines observable

        // Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ResetLogDocumentAndUIState: Calling Clear() on LogDoc (without using _uicontext.Post). thread={Environment.CurrentManagedThreadId}. Stack trace:");
        // Debug.WriteLine(Environment.StackTrace);
        LogDoc.Clear();
        FilteredLogLines.Clear();
        OnPropertyChanged(nameof(FilteredLogLinesCount)); // Notify count changed
        ScheduleLogTextUpdate(FilteredLogLines); // Clear editor
        SearchMarkers.Clear();
        _searchMatches.Clear();
        _currentSearchIndex = -1;
        OnPropertyChanged(nameof(SearchStatusText));
        HighlightedFilteredLineIndex = -1;
        HighlightedOriginalLineNumber = -1;
        TargetOriginalLineNumberInput = string.Empty; // Clear jump input
        JumpStatusMessage = string.Empty;
        IsJumpTargetInvalid = false;

        // Clear active matching status for filters
        if (ActiveFilterProfile?.RootFilterViewModel != null)
        {
            ClearActiveFilterMatchingStatusRecursive(ActiveFilterProfile.RootFilterViewModel);
        }
        // TotalLogLines is reset by _logFilterProcessor.Reset() via its observable
    }

    private bool CanPerformActionWhileNotLoading()
    {
        // Check if the loading token is NOT present
        return !CurrentBusyStates.Contains(LoadingToken);
    }

    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private async Task OpenLogFileAsync()
    {
        // 1. Stop Simulator if running
        if (IsSimulatorRunning)
        {
            ExecuteStopSimulatorLogic(); // Stop generation
                                         // Dispose simulator instance? Or keep it? Let's keep it for now.
            Debug.WriteLine("---> Stopped simulator before opening file.");
        }

        // 2. Show File Dialog
        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*", _lastOpenedFolderPath);
        if (string.IsNullOrEmpty(selectedFile)) return;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: '{selectedFile}'");

        _uiContext.Post(_ =>
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Adding LoadingToken to BusyStates.");
            CurrentBusyStates.Add(LoadingToken);
        }, null);

        try
        {
            // 3. Ensure FileLogSource instance exists (reuse or create via provider)
            _fileLogSource ??= _sourceProvider.CreateFileLogSource(); // Create if null
            if (!_disposables.Contains(_fileLogSource)) _disposables.Add(_fileLogSource); // Add if new

            _fileLogSource.StopMonitoring();

            // 5. Switch Active Source and Recreate Processor
            DisposeAndClearFilteredStream();
            CurrentActiveLogSource = _fileLogSource; // Set file source as active
            _reactiveFilteredLogStream = CreateFilteredStream(CurrentActiveLogSource);
            _disposables.Add(_reactiveFilteredLogStream);
            SubscribeToFilteredStream();

            ResetLogDocumentAndUIStateImmediate();
            CurrentLogFilePath = selectedFile;
            long initialLines = await _fileLogSource.PrepareAndGetInitialLinesAsync(selectedFile, AddLineToLogDocument).ConfigureAwait(true);
            _uiContext.Post(_ => TotalLogLines = initialLines, null);
            _fileLogSource.StartMonitoring();
            _uiContext.Post(_ => CurrentBusyStates.Add(FilteringToken), null);
            IFilter? firstFilter = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
            _reactiveFilteredLogStream.UpdateFilterSettings(firstFilter, ContextLines);
            UpdateFilterSubstrings();

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Prepare/Start completed ({initialLines} lines). First filter triggered.");

            // Store the directory of the successfully opened file
            _lastOpenedFolderPath = System.IO.Path.GetDirectoryName(selectedFile);
            SaveCurrentSettingsDelayed(); // Trigger saving the settings including the new path
        }
        catch (Exception ex)
        {
            // Error Handling (Keep existing, ensure ResetLogDocumentAndUIState is called on failure path too)
            _uiContext.Post(_ =>
            {
                CurrentBusyStates.Remove(LoadingToken);
                CurrentBusyStates.Remove(FilteringToken);
                ResetLogDocumentAndUIStateImmediate(); // Reset state on error
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Error opening file '{selectedFile}': {ex.Message}");
                MessageBox.Show($"Error opening or reading log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentLogFilePath = null;
                _fileLogSource?.StopMonitoring(); // Stop file source if it got started
            }, null);
            // Re-throw might not be needed if MessageBox is sufficient user feedback
            // throw;
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

    // Collection of filter patterns (substrings/regex) for highlighting.
    // Note: This state is derived by traversing the *active* FilterProfile.
    [ObservableProperty] private ObservableCollection<IFilter> _filterHighlightModels = new();

    private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
    {
        var newFilterModels = new ObservableCollection<IFilter>();
        if (ActiveFilterProfile?.RootFilterViewModel != null)
        {
            // Collect IFilter models instead of strings
            TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterModels);
        }
        FilterHighlightModels = newFilterModels; // Update the renamed property
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

    #region Orchestration & Updates

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
        Debug.WriteLine($"---> TriggerFilterUpdate START"); // <<< ADDED

        // Ensure token isn't added multiple times if trigger fires rapidly
        _uiContext.Post(_ =>
        {
            if (!CurrentBusyStates.Contains(FilteringToken))
            {
                CurrentBusyStates.Add(FilteringToken);
                Debug.WriteLine($"---> TriggerFilterUpdate: Added FilteringToken"); // <<< ADDED
            }
        }, null);

        _uiContext.Post(_ =>
        {
            // --- Log the filter being sent ---
            IFilter? filterToApply = ActiveFilterProfile?.Model?.RootFilter; // Get filter from model
            string filterTypeName = filterToApply?.GetType().Name ?? "null (TrueFilter assumed)";
            Debug.WriteLine($"---> TriggerFilterUpdate: Posting call to UpdateFilterSettings. Filter Type: {filterTypeName}, Context: {ContextLines}"); // <<< ADDED
            // ---

            _reactiveFilteredLogStream.UpdateFilterSettings(filterToApply ?? new TrueFilter(), ContextLines);
            UpdateFilterSubstrings(); // This reads the tree, might be good to log its result too if needed
        }, null);

        Debug.WriteLine($"---> TriggerFilterUpdate END"); // <<< ADDED
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
            foreach (var line in FilteredLogLines)
            {
                if (!first) sb.Append(Environment.NewLine);
                sb.Append(line.Text);
                first = false;
                Debug.WriteLine($"GetCurrentDocumentText (Fallback): Appended '{line.Text}'");
            }
            string result = sb.ToString();
            Debug.WriteLine($"GetCurrentDocumentText (Fallback): Returning '{result}'");
            return result;
        }
        Debug.WriteLine($"GetCurrentDocumentText (Fallback): Returning Empty String.");
        return string.Empty;
    }

    /*
    * Processes updates from the LogFilterProcessor to update the UI state.
    *
    * Responsibilities include:
    *   - Updating the FilteredLogLines collection based on update type.
    *   - Scheduling direct TextEditor.Document updates (Append/Replace) on the UI thread.
    *   - Managing busy indicators (FilteringToken, LoadingToken).
    *   - Preserving highlighted line selection during Replace updates.
    *   - Ensuring unique original line numbers during Append updates.
    *
    * Accepts explicit FilteredUpdateBase subtypes (ReplaceFilteredUpdate or AppendFilteredUpdate).
    */
    private void ApplyFilteredUpdate(FilteredUpdateBase update)
    {
        bool wasInitialLoad = CurrentBusyStates.Contains(LoadingToken);
        bool linesAdded = false; // Track if any lines were actually added in append

        if (update is AppendFilteredUpdate appendUpdate)
        {
            var linesActuallyAdded = new List<FilteredLogLine>(); // Store only the truly new lines for editor append

            // --- Check for duplicates before adding ---
            foreach (var lineToAdd in appendUpdate.Lines)
            {
                // Add only if the original line number is not already present
                if (_existingOriginalLineNumbers.Add(lineToAdd.OriginalLineNumber))
                {
                    FilteredLogLines.Add(lineToAdd); // Add to UI collection
                    linesActuallyAdded.Add(lineToAdd); // Add to list for editor update
                    linesAdded = true;
                }
                // Optional: Handle the case where a line previously added as context now arrives as a match?
                // For now, just preventing duplicates is the main goal. We could potentially update
                // the IsContextLine flag on the existing entry if needed, but it adds complexity.
            }

            // Only proceed if lines were actually added
            if (linesAdded)
            {
                OnPropertyChanged(nameof(FilteredLogLinesCount));
                ScheduleLogTextAppend(linesActuallyAdded);
                UpdateActiveFilterMatchingStatus(); // Update active matching status after appends
                if (IsAutoScrollEnabled) RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            }

            _uiContext.Post(_ => { CurrentBusyStates.Remove(FilteringToken); }, null);
        }
        else if (update is ReplaceFilteredUpdate replaceUpdate)
        {
            int originalLineToRestore = HighlightedOriginalLineNumber;

            // --- Clear tracking set before replacing ---
            _existingOriginalLineNumbers.Clear();

            ReplaceFilteredLines(replaceUpdate.Lines); // This clears FilteredLogLines

            // --- Re-populate tracking set ---
            foreach (var line in replaceUpdate.Lines)
            {
                _existingOriginalLineNumbers.Add(line.OriginalLineNumber);
            }

            ScheduleLogTextUpdate(FilteredLogLines); // Update editor with the full new text
            UpdateActiveFilterMatchingStatus();      // Update IsActivelyMatching for filters

            if (originalLineToRestore > 0)
            {
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;
                _uiContext.Post(idx => { HighlightedFilteredLineIndex = (int)idx!; }, newIndex);
            }
            else
            {
                // No line to restore, ensure highlight is cleared
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            _uiContext.Post(_ =>
            {
                CurrentBusyStates.Remove(FilteringToken);
                // Only remove LoadingToken if this update signals the *completion*
                // of the initial load processing AND we were actually in the loading state.
                if (wasInitialLoad && replaceUpdate.IsInitialLoadProcessingComplete)
                {
                    CurrentBusyStates.Remove(LoadingToken);
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} ApplyFilteredUpdate: LoadingToken removed (InitialLoadComplete).");
                }
            }, null);
        }
        else
        {
            throw new InvalidOperationException($"Unknown update type: {update.GetType().Name}");
        }
    }

    private void ScheduleLogTextAppend(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        // Ensure we work with a copy in case the original list changes
        var linesSnapshot = linesToAppend.ToList();
        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!; // Cast state first

            // Attempt to append to editor if available
            if (_logEditorInstance?.Document != null)
                AppendLogTextInternal(lines);

            // Update search matches *after* FilteredLogLines is updated (which happens before this Post)
            // and regardless of whether editor append happened. This can haååen when running from unit tests.
            UpdateSearchMatches(); // <<< Moved outside the editor check

        }, linesSnapshot);
    }

    private void AppendLogTextInternal(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        // Guard against null editor or document, or empty append list
        if (_logEditorInstance?.Document == null || !linesToAppend.Any()) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            bool needsLeadingNewline = false;

            // Check if editor has content AND if it doesn't already end with a newline
            if (_logEditorInstance.Document.TextLength > 0)
            {
                // Avoid GetText if possible for performance, check last char directly
                char lastChar = _logEditorInstance.Document.GetCharAt(_logEditorInstance.Document.TextLength - 1);
                if (lastChar != '\n' && lastChar != '\r') // Simple check, might need refinement for \r\n vs \n
                {
                    needsLeadingNewline = true;
                }
            }

            for (int i = 0; i < linesToAppend.Count; i++)
            {
                // Add newline BEFORE the line if needed (either leading or between lines)
                if (needsLeadingNewline || i > 0)
                {
                    sb.Append(Environment.NewLine);
                }
                sb.Append(linesToAppend[i].Text);
            }

            string textToAppend = sb.ToString();
            // Use BeginUpdate/EndUpdate for potentially large appends
            _logEditorInstance.Document.BeginUpdate();
            _logEditorInstance.Document.Insert(_logEditorInstance.Document.TextLength, textToAppend);
            _logEditorInstance.Document.EndUpdate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error appending text to AvalonEdit: {ex.Message}");
            // Consider how to handle errors here - maybe log to a status bar?
        }
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

    // Schedules the LogText update to run after current UI operations.
    private void ScheduleLogTextUpdate(IReadOnlyList<FilteredLogLine> relevantLines)
    {
        // Clone relevantLines if it might be modified elsewhere before Post executes?
        // ObservableCollection snapshot for Replace is safe. List passed for Append is safe.
        var linesSnapshot = relevantLines.ToList(); // Create shallow copy for safety
        _uiContext.Post(state =>
        {
            var lines = (List<FilteredLogLine>)state!; // Cast state first

            // Attempt to update editor if available
            if (_logEditorInstance?.Document != null)
            {
                ReplaceLogTextInternal(lines);
            }
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

    public void LoadLogFromText(string text)
    {
        CurrentActiveLogSource?.StopMonitoring(); // Stop current source
        _reactiveFilteredLogStream.Reset();
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LoadLogFromText: Calling Clear() on LogDoc. thread={Environment.CurrentManagedThreadId}.");
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

    private void HandleProcessorError(string contextMessage, Exception ex)
    {
        Debug.WriteLine($"{contextMessage}: {ex}");
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Clear();
            MessageBox.Show($"Error processing logs: {ex.Message}", "Processing Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }, null);
    }

    // --- Highlighting Configuration ---
    private void TraverseFilterTreeForHighlighting(FilterViewModel filterViewModel, ObservableCollection<IFilter> models)
    {
        if (!filterViewModel.Enabled) return;

        // Add the IFilter model itself if it's a SubstringFilter or RegexFilter and has a value
        if ((filterViewModel.Filter is SubstringFilter || filterViewModel.Filter is RegexFilter) &&
            !string.IsNullOrEmpty(filterViewModel.Filter.Value))
        {
            // Add the model, not just its value
            if (!models.Contains(filterViewModel.Filter)) // Avoid duplicates if same filter instance appears multiple times (unlikely with tree)
            {
                models.Add(filterViewModel.Filter);
            }
        }
        // Recursively traverse children
        foreach (var childFilterVM in filterViewModel.Children)
        {
            TraverseFilterTreeForHighlighting(childFilterVM, models);
        }
    }

    #endregion // --- Orchestration & Updates ---

    #region Filter Active Matching Status

    private void UpdateActiveFilterMatchingStatus()
    {
        if (ActiveFilterProfile?.RootFilterViewModel == null) return;

        var directMatchTexts = FilteredLogLines
            .Where(fl => !fl.IsContextLine)
            .Select(fl => fl.Text)
            .ToList(); // Materialize for multiple iterations

        UpdateMatchingStatusInternal(ActiveFilterProfile.RootFilterViewModel, directMatchTexts);
    }

    private void UpdateMatchingStatusInternal(FilterViewModel fvm, List<string> directMatchTexts)
    {
        bool isContributing = false;
        if (fvm.Enabled) // Only consider enabled filters
        {
            foreach (var text in directMatchTexts)
            {
                if (fvm.Filter.IsMatch(text))
                {
                    isContributing = true;
                    break;
                }
            }
        }
        fvm.IsActivelyMatching = isContributing;

        foreach (var child in fvm.Children)
        {
            UpdateMatchingStatusInternal(child, directMatchTexts);
        }
    }

    private void ClearActiveFilterMatchingStatusRecursive(FilterViewModel fvm)
    {
        fvm.IsActivelyMatching = false;
        foreach (var child in fvm.Children)
        {
            ClearActiveFilterMatchingStatusRecursive(child);
        }
    }

    #endregion

    #region Jump To Line State
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JumpToLineCommand))]
    private string _targetOriginalLineNumberInput = string.Empty;

    [ObservableProperty]
    private string? _jumpStatusMessage;

    [ObservableProperty]
    private bool _isJumpTargetInvalid;
    #endregion

    #region Lifecycle Management ---
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #region Undo/Redo Logic

    // --- Undo/Redo Stacks ---
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();

    // --- Undo/Redo Commands ---
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    // --- ICommandExecutor Implementation ---
    public void Execute(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action.Execute();
        _undoStack.Push(action);
        _redoStack.Clear(); // Clear redo stack on new action

        // Update CanExecute state for UI buttons
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();

        // Trigger necessary updates AFTER the action is executed
        TriggerFilterUpdate(); // Re-filter based on the new state
        SaveCurrentSettingsDelayed(); // Save the new state
    }

    private void Undo()
    {
        if (_undoStack.TryPop(out var action))
        {
            action.Undo();
            _redoStack.Push(action);
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();

            TriggerFilterUpdate(); // Re-filter based on the restored state
            SaveCurrentSettingsDelayed(); // Save the restored state
        }
    }
    private bool CanUndo() => _undoStack.Count > 0;

    private void Redo()
    {
        if (_redoStack.TryPop(out var action))
        {
            action.Execute(); // Re-execute the action
            _undoStack.Push(action);
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();

            TriggerFilterUpdate(); // Re-filter based on the re-applied state
            SaveCurrentSettingsDelayed(); // Save the re-applied state
        }
    }
    private bool CanRedo() => _redoStack.Count > 0;

    #endregion

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
        CurrentActiveLogSource?.StopMonitoring(); // Explicitly stop monitoring before disposing
        Dispose(); // Calls the main Dispose method which cleans everything else
    }
    #endregion // --- Lifecycle Management ---
}
