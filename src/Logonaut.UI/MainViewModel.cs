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
using System.ComponentModel; // For PropertyChangedEventArgs

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
public partial class MainViewModel : ObservableObject, IDisposable
{
    #region Fields

    // --- Services & Context ---
    private readonly IScheduler? _backgroundScheduler; // Make it possible to inject your own background scheduler
    private readonly ILogSourceProvider _sourceProvider;
    public static readonly object LoadingToken = new();
    public static readonly object FilteringToken = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private ILogSource _currentActiveLogSource;
    private readonly SynchronizationContext _uiContext;
    private ILogFilterProcessor _logFilterProcessor; // This will be recreated when switching sources

    // --- Simulator Specific Fields ---
    private ISimulatorLogSource? _simulatorLogSource; // Keep a dedicated instance
    private ILogSource? _fileLogSource; // Keep a reference to the file source instance

    // --- Lifecycle Management ---
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _filterSubscription;
    private IDisposable? _totalLinesSubscription;

    // --- Search State & Ruler Markers ---
    private List<SearchResult> _searchMatches = new();
    private int _currentSearchIndex = -1;
    [ObservableProperty] private ObservableCollection<SearchResult> _searchMarkers = new();
    [ObservableProperty] private bool _isAutoScrollEnabled = true;
    public event EventHandler? RequestScrollToEnd; // Triggered when Auto Scroll is enabled
    public event EventHandler<int>? RequestScrollToLineIndex; // Event passes the 0-based index to scroll to

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
        _currentActiveLogSource = _fileLogSource; // Start with file source active
        _disposables.Add(_fileLogSource); // Add file source to disposables

        // Create the initial processor with the file source
        _logFilterProcessor = CreateProcessor(_currentActiveLogSource);
        _disposables.Add(_logFilterProcessor); // Add processor to disposables
        SubscribeToProcessor(); // Subscribe to the initial processor

        CurrentBusyStates.CollectionChanged += CurrentBusyStates_CollectionChanged;
        _disposables.Add(Disposable.Create(() => {
            CurrentBusyStates.CollectionChanged -= CurrentBusyStates_CollectionChanged;
        }));

        Theme = new ThemeViewModel();
        LoadPersistedSettings(); // Loads basic settings, not files yet
    }

    private ILogFilterProcessor CreateProcessor(ILogSource source)
    {
        Debug.WriteLine($"---> Creating LogFilterProcessor with source: {source.GetType().Name}");
        return new LogFilterProcessor(
            source,
            LogDoc,
            _uiContext,
            AddLineToLogDocument,
            _backgroundScheduler);
    }

    private void SubscribeToProcessor()
    {
        // Dispose previous subscriptions if they exist
        _filterSubscription?.Dispose();
        _totalLinesSubscription?.Dispose();

        Debug.WriteLine($"---> Subscribing to processor: {_logFilterProcessor.GetType().Name}");

        _filterSubscription = _logFilterProcessor.FilteredUpdates
            .ObserveOn(_uiContext) // Ensure UI thread for updates
            .Subscribe(
                update => ApplyFilteredUpdate(update),
                ex => HandleProcessorError("Log Processing Error", ex)
            );

        var samplingScheduler = Scheduler.Default;
        _totalLinesSubscription = _logFilterProcessor.TotalLinesProcessed
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
    private void DisposeAndClearProcessor()
    {
        Debug.WriteLine($"---> Disposing processor and subscriptions.");
        _filterSubscription?.Dispose();
        _totalLinesSubscription?.Dispose();
        _filterSubscription = null;
        _totalLinesSubscription = null;

        // Assuming processor was added to _disposables when created
        if (_logFilterProcessor != null)
        {
            _disposables.Remove(_logFilterProcessor); // Remove from main collection before disposing
            _logFilterProcessor.Dispose();
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

    #region Simulator Configuration UI State & Properties

    [ObservableProperty] private bool _isSimulatorConfigurationVisible = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSimulatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopSimulatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartSimulatorCommand))]
    private bool _isSimulatorRunning = false;

    [ObservableProperty]
    private double _simulatorLPS = 10; // Use double for Slider binding

    partial void OnSimulatorLPSChanged(double value)
    {
        // Update the running simulator's rate immediately via interface
        _simulatorLogSource?.UpdateRate((int)Math.Round(value));
    }

    #endregion // Simulator Configuration UI State & Properties

    #region Simulator Control Commands

    [RelayCommand(CanExecute = nameof(CanStartSimulator))]
    private void StartSimulator()
    {
        if (IsSimulatorRunning) return;

        try
        {
            if (_currentActiveLogSource == _fileLogSource) _fileLogSource?.StopMonitoring();

            // 2. Get Simulator Instance via Provider
            _simulatorLogSource = _sourceProvider.CreateSimulatorLogSource(); // <<<< Returns ISimulatorLogSource
            if (!_disposables.Contains((IDisposable)_simulatorLogSource)) _disposables.Add((IDisposable)_simulatorLogSource);
            _simulatorLogSource.Stop(); // Call Stop via interface

            // 3. Configure Simulator
            _simulatorLogSource.LinesPerSecond = (int)Math.Round(SimulatorLPS); // Set property via interface

            // 4. Switch Active Source and Recreate Processor
            DisposeAndClearProcessor();
            _currentActiveLogSource = _simulatorLogSource; // Assign ISimulatorLogSource (which is also ILogSource)
            _logFilterProcessor = CreateProcessor(_currentActiveLogSource);
            _disposables.Add(_logFilterProcessor);
            SubscribeToProcessor();

            // 5. Reset Document and UI State
            ResetLogDocumentAndUIState();
            CurrentLogFilePath = "[Simulation Active]";

            // 6. Prepare and Start Simulator Source
            _simulatorLogSource.PrepareAndGetInitialLinesAsync("Simulator", AddLineToLogDocument).Wait();
            _simulatorLogSource.Start(); // Call Start via interface

            IsSimulatorRunning = _simulatorLogSource.IsRunning; // Use IsRunning property via interface
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error starting simulator", ex);
        }
    }
    private bool CanStartSimulator() => !IsSimulatorRunning;

    [RelayCommand(CanExecute = nameof(CanStopSimulator))]
    private void StopSimulator()
    {
        if (!IsSimulatorRunning) return;

        try
        {
            _simulatorLogSource?.Stop(); // Call Stop via interface
            IsSimulatorRunning = _simulatorLogSource?.IsRunning ?? false;
            Debug.WriteLine("---> Simulator Stopped");
            // Do not switch back to file source automatically here.
            // Keep CurrentLogFilePath as "[Simulation Active]"? Or clear it?
             // CurrentLogFilePath = "[Simulation Stopped]";
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error stopping simulator", ex);
        }
    }
    private bool CanStopSimulator() => IsSimulatorRunning;

    [RelayCommand(CanExecute = nameof(CanStopSimulator))] // Can only restart if running
    private void RestartSimulator()
    {
        if (!IsSimulatorRunning) return; // Should be handled by CanExecute, but double-check

        try
        {
            ResetLogDocumentAndUIState();
            _simulatorLogSource?.Restart(); // Call Restart via interface
            IsSimulatorRunning = _simulatorLogSource?.IsRunning ?? false;
            Debug.WriteLine("---> Simulator Restarted");
        }
         catch (Exception ex)
        {
            HandleSimulatorError("Error restarting simulator", ex);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        try
        {
            ResetLogDocumentAndUIState();
            // If the simulator is running, it keeps running, just the view is cleared
            if (IsSimulatorRunning)
            {
                // Optionally restart simulator counter? Depends on desired behavior.
                // _simulatorLogSource?.Restart(); // Uncomment if restart desired on Clear
            }
            // Manually trigger a filter update on the now empty document
             TriggerFilterUpdate();
            Debug.WriteLine("---> Log Cleared");
        }
        catch (Exception ex)
        {
            // Handle potential errors during clearing (unlikely)
             Debug.WriteLine($"!!! Error clearing log: {ex.Message}");
             MessageBox.Show($"Error clearing log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Helper to reset document and related UI state
    private void ResetLogDocumentAndUIState()
    {
        // Reset Core State
        _logFilterProcessor.Reset(); // Resets processor's internal index and total lines observable

        // Clear Document and UI Collections/State
        _uiContext.Post(_ => {
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
            // TotalLogLines is reset by _logFilterProcessor.Reset() via its observable
        }, null);
    }

    private void HandleSimulatorError(string context, Exception ex)
    {
        Debug.WriteLine($"!!! {context}: {ex.Message}");
        MessageBox.Show($"{context}: {ex.Message}", "Simulator Error", MessageBoxButton.OK, MessageBoxImage.Error);
        // Optionally stop the simulator on error
        if (IsSimulatorRunning)
        {
            StopSimulator();
        }
    }

    #endregion // Simulator Control Commands

    #region Command Handling

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
        // 1. Stop Simulator if running
        if (IsSimulatorRunning)
        {
            StopSimulator(); // Stop generation
            // Dispose simulator instance? Or keep it? Let's keep it for now.
             Debug.WriteLine("---> Stopped simulator before opening file.");
        }

        // 2. Show File Dialog
        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
        if (string.IsNullOrEmpty(selectedFile)) return;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: '{selectedFile}'");

        _uiContext.Post(_ => CurrentBusyStates.Add(LoadingToken), null);

        try
        {
            // 3. Ensure FileLogSource instance exists (reuse or create via provider)
            _fileLogSource ??= _sourceProvider.CreateFileLogSource(); // Create if null
            if (!_disposables.Contains(_fileLogSource)) _disposables.Add(_fileLogSource); // Add if new

            // ... rest of OpenLogFileAsync uses _fileLogSource ...
             _fileLogSource.StopMonitoring();

            // 5. Switch Active Source and Recreate Processor
            DisposeAndClearProcessor();
            _currentActiveLogSource = _fileLogSource; // Set file source as active
            _logFilterProcessor = CreateProcessor(_currentActiveLogSource);
            _disposables.Add(_logFilterProcessor);
            SubscribeToProcessor();

            // ... rest of the method (ResetLogDocumentAndUIState, Prepare, Start, TriggerFilter) ...
             ResetLogDocumentAndUIState();
             CurrentLogFilePath = selectedFile;
             long initialLines = await _fileLogSource.PrepareAndGetInitialLinesAsync(selectedFile, AddLineToLogDocument).ConfigureAwait(true);
             _uiContext.Post(_ => TotalLogLines = initialLines, null);
             _fileLogSource.StartMonitoring();
             _uiContext.Post(_ => CurrentBusyStates.Add(FilteringToken), null);
             IFilter? firstFilter = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
             _logFilterProcessor.UpdateFilterSettings(firstFilter, ContextLines);
             UpdateFilterSubstrings();

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Prepare/Start completed ({initialLines} lines). First filter triggered.");
        }
        catch (Exception ex)
        {
             // Error Handling (Keep existing, ensure ResetLogDocumentAndUIState is called on failure path too)
             _uiContext.Post(_ => {
                 CurrentBusyStates.Remove(LoadingToken);
                 CurrentBusyStates.Remove(FilteringToken);
                 ResetLogDocumentAndUIState(); // Reset state on error
                 Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Error opening file '{selectedFile}': {ex.Message}");
                 MessageBox.Show($"Error opening or reading log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 CurrentLogFilePath = null;
                 _fileLogSource?.StopMonitoring(); // Stop file source if it got started
             }, null);
            // Re-throw might not be needed if MessageBox is sufficient user feedback
             // throw;
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
    *   - Updating the FilteredLogLines collection based on update type.
    *   - Scheduling direct TextEditor.Document updates (Append/Replace) on the UI thread.
    *   - Managing busy indicators (FilteringToken, LoadingToken).
    *   - Preserving highlighted line selection during Replace updates.
    *
    * Accepts explicit FilteredUpdateBase subtypes (ReplaceFilteredUpdate or AppendFilteredUpdate).
    */
    private void ApplyFilteredUpdate(FilteredUpdateBase update)
    {
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate received: {update.GetType().Name}. Lines={update.Lines.Count}");
        bool wasInitialLoad = CurrentBusyStates.Contains(LoadingToken);

        if (update is AppendFilteredUpdate appendUpdate) // <<< Use type pattern matching
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate: Handling Append.");

            // 1. Add only the new lines from the Append update
            AddFilteredLines(appendUpdate.Lines); // This contains ONLY the new/context lines
            // 2. Schedule the append text operation for AvalonEdit
            ScheduleLogTextAppend(appendUpdate.Lines);
            // 3. Trigger Auto-Scroll if enabled
            if (IsAutoScrollEnabled) RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            // 4. Reset BusyFiltering (Append means this batch is done)
            _uiContext.Post(_ => { CurrentBusyStates.Remove(FilteringToken); }, null);
        }
        else if (update is ReplaceFilteredUpdate replaceUpdate) // <<< Use type pattern matching
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> ApplyFilteredUpdate: Handling Replace.");
            int originalLineToRestore = HighlightedOriginalLineNumber;
            // 1. Replace the entire ObservableCollection with lines from Replace update
            ReplaceFilteredLines(replaceUpdate.Lines);
            // 2. Schedule the full text replace for AvalonEdit
            ScheduleLogTextUpdate(FilteredLogLines); // Pass the *updated* collection
            // 3. Restore Highlight (only makes sense after a replace)
            if (originalLineToRestore > 0)
            {
                int newIndex = FilteredLogLines
                    .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                    .FirstOrDefault(item => item.OriginalLineNumber == originalLineToRestore)?.Index ?? -1;
                // Post the update to the UI thread
                _uiContext.Post(idx => { HighlightedFilteredLineIndex = (int)idx!; }, newIndex);
            }
            else
            {
                 // Post the update to the UI thread
                _uiContext.Post(_ => { HighlightedFilteredLineIndex = -1; }, null);
            }

            // 4. Replace means filtering is done AND if it was the initial load, loading is also done.
            _uiContext.Post(_ => {
                CurrentBusyStates.Remove(FilteringToken);
                if (wasInitialLoad) CurrentBusyStates.Remove(LoadingToken);
            }, null);
        }
        else
        {
            // Should not happen if processor only emits known types
            Debug.WriteLine($"WARN: ApplyFilteredUpdate received unknown update type: {update.GetType().Name}");
        }
    }

    private void ScheduleLogTextAppend(IReadOnlyList<FilteredLogLine> linesToAppend)
    {
        // Ensure we work with a copy in case the original list changes
        var linesSnapshot = linesToAppend.ToList();
        _uiContext.Post(state => {
            if (_logEditorInstance?.Document == null) {
                Debug.WriteLine("WARN: ScheduleLogTextAppend skipped - editor instance or document is null.");
                return; // Skip if editor isn't set or document isn't ready
            }
            var lines = (List<FilteredLogLine>)state!;
            AppendLogTextInternal(lines);
            // Update search matches *after* appending text
            UpdateSearchMatches();
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
            if (_logEditorInstance?.Document == null) {
                Debug.WriteLine("WARN: ScheduleLogTextUpdate skipped - editor instance or document is null.");
                return; // Skip if editor isn't set or document isn't ready
            }
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
        _currentActiveLogSource?.StopMonitoring(); // Stop current source
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
                var regexIssues = false;
                if (isRegex && new Regex(pattern).IsMatch(string.Empty))
                    regexIssues = true;
                if (!regexIssues && !patterns.Contains(pattern))
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

    #region Lifecycle Management ---
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
         _currentActiveLogSource?.StopMonitoring(); // Explicitly stop monitoring before disposing
        Dispose(); // Calls the main Dispose method which cleans everything else
    }
    #endregion // --- Lifecycle Management ---
}

/// <summary>
/// Represents the position and length of a found search match within the text.
/// Used for internal tracking and for markers on the OverviewRuler.
/// </summary>
public record SearchResult(int Offset, int Length);
