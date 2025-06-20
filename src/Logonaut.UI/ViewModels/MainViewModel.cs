using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reflection;
using System.IO; // For Stream
using System.Windows; // For Visibility
using System.ComponentModel; // For PropertyChangedEventArgs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Services;
using Logonaut.Core.Commands;
using ICSharpCode.AvalonEdit;

namespace Logonaut.UI.ViewModels;

/*
 * Main ViewModel for the Logonaut application.
 * Orchestrates overall application logic, including settings management, log data processing
 * for the active view (via TabViewModel), filter profile management, and UI state.
 * It acts as the central point for data binding and command execution for the main window.
 */
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    #region Fields

    private const string WelcomeTabIdentifier = "welcome_tab";
    private readonly IScheduler? _backgroundScheduler;
    private readonly ILogSourceProvider _sourceProvider;
    // Global Busy States - For operations not tied to a single tab (e.g., initial app load, global settings save)
    public static readonly object GlobalLoadingToken = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;
    private readonly SynchronizationContext _uiContext;

    private readonly CompositeDisposable _disposables = new();
    public event EventHandler? RequestGlobalScrollToEnd; // If needed for a global "tail" concept
    public event EventHandler<int>? RequestGlobalScrollToLineIndex; // If needed

    public ThemeViewModel Theme { get; }

    private bool _settingsDirty = false; // Flag to track if settings need saving

    #endregion

    #region Tab Management
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLogLines))]
    [NotifyPropertyChangedFor(nameof(FilteredLogLinesCount))]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    [NotifyPropertyChangedFor(nameof(HighlightedOriginalLineNumber))]
    [NotifyPropertyChangedFor(nameof(HighlightedFilteredLineIndex))]
    [NotifyPropertyChangedFor(nameof(TargetOriginalLineNumberInput))]
    [NotifyPropertyChangedFor(nameof(JumpStatusMessage))]
    [NotifyPropertyChangedFor(nameof(IsJumpTargetInvalid))]
    [NotifyPropertyChangedFor(nameof(FilterHighlightModels))]
    [NotifyPropertyChangedFor(nameof(SearchMarkers))]
    [NotifyPropertyChangedFor(nameof(CurrentMatchOffset))]
    [NotifyPropertyChangedFor(nameof(CurrentMatchLength))]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    private TabViewModel? _activeTabViewModel;

    public ObservableCollection<TabViewModel> TabViewModels { get; } = new();
    #endregion

    #region Stats Properties (Now Delegated or Global)

    // These now delegate to the ActiveTabViewModel for Phase 1
    public long TotalLogLines => ActiveTabViewModel?.TotalLogLines ?? 0;
    public int FilteredLogLinesCount => ActiveTabViewModel?.FilteredLogLinesCount ?? 0;

    #endregion

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

        UndoCommand = new RelayCommand(Undo, CanUndo);
        RedoCommand = new RelayCommand(Redo, CanRedo);

        CurrentGlobalBusyStates.CollectionChanged += CurrentGlobalBusyStates_CollectionChanged;
        _disposables.Add(Disposable.Create(() =>
        {
            CurrentGlobalBusyStates.CollectionChanged -= CurrentGlobalBusyStates_CollectionChanged;
        }));

        Theme = new ThemeViewModel();

        // Load settings first to determine initial state, including active filter profile
        LoadPersistedSettings();

        // Create an initial, empty tab.
        var initialTab = new TabViewModel(
            initialHeader: "Welcome",
            initialAssociatedProfileName: ActiveFilterProfile?.Name ?? "Default",
            initialSourceType: SourceType.Pasted,
            initialSourceIdentifier: WelcomeTabIdentifier, // A unique identifier
            _sourceProvider,
            this,
            _uiContext,
            _backgroundScheduler
        );
        AddTab(initialTab);
        ActiveTabViewModel = initialTab;

        // "Activate" the initial tab
        _ = initialTab.ActivateAsync(
            this.AvailableProfiles,
            this.ContextLines,
            this.HighlightTimestamps,
            this.ShowLineNumbers,
            this.IsAutoScrollEnabled,
            null // No specific restart handler for a welcome tab
        ).ContinueWith(t => {
            if (t.IsFaulted) Debug.WriteLine($"Error activating initial tab: {t.Exception}");
        });


        PopulateFilterPalette(); // Remains in MainViewModel as palette is global

        ToggleAboutOverlayCommand = new RelayCommand(ExecuteToggleAboutOverlay);
        LoadRevisionHistory();
    }

    #endregion

    #region Tab Lifecycle
    private void AddTab(TabViewModel tab)
    {
        TabViewModels.Add(tab);
        tab.RequestCloseTab += OnTabRequestClose;
        tab.PropertyChanged += OnTabPropertyChanged;
        tab.SourceRestartDetected += HandleTabSourceRestart;
        _disposables.Add(tab); // Ensure it gets disposed
    }

    /*
     * Checks if the only tab present is the "Welcome" tab and, if so, removes it.
     * This is called before creating the first user-initiated tab (e.g., from opening a file or pasting text)
     * to provide a seamless transition from the welcome screen to the user's content.
     */
    private void HandleWelcomeTabReplacement()
    {
        // Check if the only tab present is the "Welcome" tab.
        if (TabViewModels.Count == 1 && TabViewModels[0].SourceIdentifier == WelcomeTabIdentifier)
        {
            var welcomeTab = TabViewModels[0];
            Debug.WriteLine("---> MainViewModel: Replacing the initial 'Welcome' tab.");
    
            // Unsubscribe from events to prevent memory leaks.
            welcomeTab.RequestCloseTab -= OnTabRequestClose;
            welcomeTab.PropertyChanged -= OnTabPropertyChanged;
            welcomeTab.SourceRestartDetected -= HandleTabSourceRestart;
    
            // Directly remove it from the collection.
            TabViewModels.Remove(welcomeTab);
    
            // Dispose it to release its resources (processor, subscriptions, etc.).
            welcomeTab.Dispose();
        }
    }

    private void CloseTab(TabViewModel tab)
    {
        if (tab == null) return;

        tab.RequestCloseTab -= OnTabRequestClose;
        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.SourceRestartDetected -= HandleTabSourceRestart;

        int closingTabIndex = TabViewModels.IndexOf(tab);
        TabViewModels.Remove(tab);
        tab.Dispose(); // Release its resources

        // If we closed the active tab, select a new one
        if (ActiveTabViewModel == null || ActiveTabViewModel == tab)
        {
            if (TabViewModels.Count > 0)
            {
                // Select the previous tab, or the first one if the closed tab was the first
                ActiveTabViewModel = TabViewModels[Math.Max(0, closingTabIndex - 1)];
            }
            else
            {
                // If the last tab is closed, create a new empty one
                var newEmptyTab = new TabViewModel("New Tab", ActiveFilterProfile?.Name ?? "Default", SourceType.Pasted, $"empty_{Guid.NewGuid()}", _sourceProvider, this, _uiContext, _backgroundScheduler);
                AddTab(newEmptyTab);
                ActiveTabViewModel = newEmptyTab;
                _ = newEmptyTab.ActivateAsync(AvailableProfiles, ContextLines, HighlightTimestamps, ShowLineNumbers, IsAutoScrollEnabled, null);
            }
        }
    }

    private void OnTabRequestClose(object? sender, EventArgs e)
    {
        if (sender is TabViewModel tab)
        {
            CloseTab(tab);
        }
    }
    #endregion


    partial void OnActiveTabViewModelChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        Debug.WriteLine($"---> MainViewModel: Active tab changed from '{oldValue?.Header}' to '{newValue?.Header}'");

        if (oldValue != null)
        {
            // Unsubscribe from events of the old tab
            oldValue.RequestScrollToEnd -= ViewModel_RequestScrollToEnd;
            oldValue.RequestScrollToLineIndex -= ViewModel_RequestScrollToLineIndex;
            oldValue.FilteredLinesUpdated -= ActiveTabViewModel_FilteredLinesUpdated;

            // Deactivate the old tab
            // oldValue.DeactivateLogProcessing(); // Step 2.1
        }

        if (newValue != null)
        {
            // Subscribe to events of the new tab
            newValue.RequestScrollToEnd += ViewModel_RequestScrollToEnd;
            newValue.RequestScrollToLineIndex += ViewModel_RequestScrollToLineIndex;
            newValue.FilteredLinesUpdated += ActiveTabViewModel_FilteredLinesUpdated;

            // Apply global settings to the new active tab
            ApplyGlobalSettingsToTab(newValue);

            // Activate the new tab
            // _ = newValue.ActivateAsync(...); // Step 2.1
        }
    }

   private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // If the property change is from the active tab, forward it to MainViewModel's listeners
        if (sender is TabViewModel activeTab && activeTab == ActiveTabViewModel)
        {
            // Specific handling for properties that MainViewModel cares about
            if (e.PropertyName == nameof(TabViewModel.SelectedText))
            {
                // Update the property that the Filter Palette is bound to.
                this.SelectedLogTextForFilter = activeTab.SelectedText;
            }
            else
            {
                // For other properties (like IsLoading, SearchStatusText, etc.),
                // use the shotgun approach to notify the status bar and other global UI elements.
                OnPropertyChanged(e.PropertyName);
            }
        }
    }

    private void HandleTabSourceRestart(TabViewModel snapshotTab, string restartedFilePath)
    {
        // This is the handler for Step 2.5
        // For now, we'll just log it. The logic will be implemented later.
        Debug.WriteLine($"---> MainViewModel: Received SourceRestartDetected from tab '{snapshotTab.Header}' for file '{restartedFilePath}'.");
        // Future logic: Create and open a new tab for restartedFilePath.
    }

    /*
    * Handles a paste command from the UI. It checks the clipboard for text content
    * and, if found, initiates the process of loading that text into a tab.
    * This is typically bound to a global hotkey like Ctrl+V.
    */
    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private void Paste()
    {
        Debug.WriteLine("Pasting text from clipboard...");
        if (Clipboard.ContainsText())
        {
            string text = Clipboard.GetText();
            // The LoadLogFromText method will handle creating a new tab
            LoadLogFromText(text);
        }
    }

    #region About command
    [ObservableProperty] private bool _isAboutOverlayVisible;
    [ObservableProperty] private string? _aboutRevisionHistory;
    public static string ApplicationVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
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
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                AboutRevisionHistory = "Error: Revision history resource not found.";
                Debug.WriteLine($"Error: Could not find embedded resource '{resourceName}'");
                return;
            }
            using StreamReader reader = new StreamReader(stream);
            AboutRevisionHistory = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            AboutRevisionHistory = $"Error loading revision history: {ex.Message}";
            Debug.WriteLine($"Exception loading revision history: {ex}");
        }
    }
    #endregion

    const int MaxPaletteDisplayTextLength = 20;

    // --- Global Busy State ---
    public ObservableCollection<object> CurrentGlobalBusyStates { get; } = new();
    public bool IsGloballyLoading => CurrentGlobalBusyStates.Any(); // Simplified global loading state

    private void CurrentGlobalBusyStates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsGloballyLoading));
    }

    private void ActiveTabViewModel_FilteredLinesUpdated(object? sender, EventArgs e)
    {
        if (sender == ActiveTabViewModel)
        {
            Debug.WriteLine($"---> MainViewModel: Received FilteredLinesUpdated from Active Tab. Calling UpdateActiveFilterMatchingStatus.");
            _uiContext.Post(_ => UpdateActiveFilterMatchingStatus(), null); // Ensure UI thread for safety
        }
    }

    #region Delegated Properties and Commands for UI Binding

    // These properties allow existing UI bindings to MainViewModel to continue working by delegating to ActiveTabViewModel.
    public ObservableCollection<FilteredLogLine>? FilteredLogLines => ActiveTabViewModel?.FilteredLogLines;
    public ObservableCollection<IFilter>? FilterHighlightModels => ActiveTabViewModel?.FilterHighlightModels;

    public int HighlightedFilteredLineIndex
    {
        get => ActiveTabViewModel?.HighlightedFilteredLineIndex ?? -1;
        set { if (ActiveTabViewModel != null) ActiveTabViewModel.HighlightedFilteredLineIndex = value; }
    }
    public int HighlightedOriginalLineNumber
    {
        get => ActiveTabViewModel?.HighlightedOriginalLineNumber ?? -1;
        set { if (ActiveTabViewModel != null) ActiveTabViewModel.HighlightedOriginalLineNumber = value; }
    }

    public string TargetOriginalLineNumberInput
    {
        get => ActiveTabViewModel?.TargetOriginalLineNumberInput ?? string.Empty;
        set { if (ActiveTabViewModel != null) ActiveTabViewModel.TargetOriginalLineNumberInput = value; }
    }
    public string? JumpStatusMessage => ActiveTabViewModel?.JumpStatusMessage;
    public bool IsJumpTargetInvalid => ActiveTabViewModel?.IsJumpTargetInvalid ?? false;
    public IRelayCommand? JumpToLineCommand => ActiveTabViewModel?.JumpToLineCommand;

    public string SearchText
    {
        get => ActiveTabViewModel?.SearchText ?? string.Empty;
        set { if (ActiveTabViewModel != null) ActiveTabViewModel.SearchText = value; }
    }
    public bool IsCaseSensitiveSearch
    {
        get => ActiveTabViewModel?.IsCaseSensitiveSearch ?? false;
        set
        {
            if (ActiveTabViewModel != null && ActiveTabViewModel.IsCaseSensitiveSearch != value)
            {
                ActiveTabViewModel.IsCaseSensitiveSearch = value;
                OnPropertyChanged(); // Notify UI if MainViewModel has direct binding
                MarkSettingsAsDirty(); // Global setting change
            }
        }
    }
    public ObservableCollection<SearchResult>? SearchMarkers => ActiveTabViewModel?.SearchMarkers;
    public int CurrentMatchOffset => ActiveTabViewModel?.CurrentMatchOffset ?? -1;
    public int CurrentMatchLength => ActiveTabViewModel?.CurrentMatchLength ?? 0;
    public string SearchStatusText => ActiveTabViewModel?.SearchStatusText ?? string.Empty;
    public IRelayCommand? PreviousSearchCommand => ActiveTabViewModel?.PreviousSearchCommand;
    public IRelayCommand? NextSearchCommand => ActiveTabViewModel?.NextSearchCommand;
    
    // For BusyIndicator specific to the tab's loading/filtering
    public ObservableCollection<object>? CurrentBusyStates => ActiveTabViewModel?.CurrentBusyStates;
    public bool IsLoading => ActiveTabViewModel?.IsLoading ?? false;

    // CurrentLogFilePath is a bit special. MainViewModel still shows it globally.
    [ObservableProperty] private string? _currentGlobalLogFilePathDisplay;

    #endregion

    private void ViewModel_RequestScrollToEnd(object? sender, EventArgs e)
    {
        RequestGlobalScrollToEnd?.Invoke(sender, e);
    }
    private void ViewModel_RequestScrollToLineIndex(object? sender, int e)
    {
        RequestGlobalScrollToLineIndex?.Invoke(sender, e);
    }


    private void LoadPersistedSettings()
    {
        // Add GlobalLoadingToken:
        _uiContext.Post(_ => CurrentGlobalBusyStates.Add(GlobalLoadingToken), null);

        LogonautSettings settings = _settingsService.LoadSettings();
        Debug.WriteLine($"---> MainViewModel: Loaded settings from {settings.WindowWidth}");
        LoadFilterProfiles(settings); // This sets ActiveFilterProfile

        _lastOpenedFolderPath = settings.LastOpenedFolderPath;
        LoadUiSettings(settings); // Loads ContextLines, ShowLineNumbers etc.
        LoadSimulatorPersistedSettings(settings);

        // Load window geometry and filter panel width into MainViewModel properties
        WindowTop = settings.WindowTop;
        WindowLeft = settings.WindowLeft;
        WindowHeight = settings.WindowHeight;
        WindowWidth = settings.WindowWidth;
        FilterPanelWidth = settings.FilterPanelWidth;
        WindowState = settings.WindowState;
        Debug.WriteLine($"---> MainViewModel: Applied geometry to ViewModel. VM.WindowWidth: {WindowWidth}, VM.FilterPanelWidth: {FilterPanelWidth}");

        _uiContext.Post(_ => CurrentGlobalBusyStates.Remove(GlobalLoadingToken), null);
        _settingsDirty = false; // Settings are now in sync with persisted state
    }

    /*
     * Marks the settings as dirty, indicating they need to be saved.
     * This method is called whenever a setting that should be persisted is changed.
     */
    public void MarkSettingsAsDirty()
    {
        _settingsDirty = true;
    }

    /*
     * Saves all current application settings to persistent storage.
     * This method constructs a LogonautSettings object with the current state
     * of all configurable options and uses the ISettingsService to write it.
     * It should be called when the application is closing or when an explicit save is triggered.
     */
    public void SaveCurrentSettings()
    {
        Debug.WriteLine("---> MainViewModel: SaveCurrentSettings called.");
        var settingsToSave = new LogonautSettings
        {
            LastOpenedFolderPath = this._lastOpenedFolderPath,
        };

        // Populate window geometry from MainViewModel properties (which MainWindow will update)
        settingsToSave.WindowTop = WindowTop;
        settingsToSave.WindowLeft = WindowLeft;
        settingsToSave.WindowHeight = WindowHeight;
        settingsToSave.WindowWidth = WindowWidth;
        settingsToSave.FilterPanelWidth = FilterPanelWidth;
        settingsToSave.WindowState = WindowState;

        SaveUiSettings(settingsToSave);
        SaveSimulatorSettings(settingsToSave);
        SaveFilterProfiles(settingsToSave);
        _settingsService.SaveSettings(settingsToSave);
        _settingsDirty = false; // Reset dirty flag after saving
        Debug.WriteLine("---> MainViewModel: Settings saved successfully.");
    }

    private bool CanPerformActionWhileNotLoading()
    {
        bool tabIsLoading = ActiveTabViewModel?.IsLoading ?? false;
        bool mainIsLoading = IsGloballyLoading;
        bool canPerform = !mainIsLoading && !tabIsLoading;
        Debug.WriteLine($"---> MainViewModel.CanPerformActionWhileNotLoading: Result={canPerform}. MainLoading={mainIsLoading}, TabLoading={tabIsLoading}");
        return canPerform;
    }

    #region Orchestration & Updates (Altered)

    private void TriggerFilterUpdate()
    {
        if (ActiveTabViewModel == null) return;
        
        // The active tab is responsible for applying its own filters.
        // We just need to tell it that a global setting it depends on has changed.
        ApplyGlobalSettingsToTab(ActiveTabViewModel);
    }

    private void ApplyGlobalSettingsToTab(TabViewModel tab)
    {
        // This method will be used to push global settings (like ContextLines, which profile to use, etc.)
        // to a tab, typically when it becomes active or when a global setting changes.
        tab.AssociatedFilterProfileName = ActiveFilterProfile?.Name ?? "Default";
        tab.ApplyFiltersFromProfile(this.AvailableProfiles, this.ContextLines);

        // Update other global settings
        // tab.ShowLineNumbers = this.ShowLineNumbers; // Future: Move these to TabViewModel
        // tab.HighlightTimestamps = this.HighlightTimestamps;

        // Update FilterHighlightModels on the active tab
        var newFilterModels = new ObservableCollection<IFilter>();
        if (ActiveFilterProfile?.RootFilterViewModel != null)
        {
            TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterModels);
        }
        tab.FilterHighlightModels = newFilterModels;

        // Update matching status based on the now-active tab
        UpdateActiveFilterMatchingStatus();
    }


    // GetCurrentDocumentText for MainViewModel (e.g., for filter matching status)
    // should now get it from the active tab's perspective.
    private string GetCurrentDocumentText()
    {
        // This is for MainViewModel's direct needs, like UpdateActiveFilterMatchingStatus.
        if (ActiveTabViewModel?.FilteredLogLines != null && ActiveTabViewModel.FilteredLogLines.Any())
        {
            return string.Join(Environment.NewLine, ActiveTabViewModel.FilteredLogLines.Select(fll => fll.Text));
        }
        return string.Empty;
    }

    #endregion

    #region Lifecycle Management ---
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeProfileNameSubscription?.Dispose();
            _disposables.Dispose(); // Disposes all added IDisposables, including all tabs
        }
    }
    /*
     * Performs cleanup operations when the application is closing.
     * This includes saving any pending settings changes if the settings are marked as dirty.
     * It ensures that the latest user configurations are persisted before the application exits.
     */
    public void Cleanup()
    {
        _uiContext.Post(_ => CurrentGlobalBusyStates.Clear(), null);
        if (_settingsDirty)
        {
            SaveCurrentSettings();
        }
        Dispose();
    }
    #endregion

    #region Window Geometry Properties (for MainWindow to update)
    [ObservableProperty] private double _windowTop;
    [ObservableProperty] private double _windowLeft;
    [ObservableProperty] private double _windowHeight;
    [ObservableProperty] private double _windowWidth;
    [ObservableProperty] private double _filterPanelWidth;
    [ObservableProperty] private AppWindowState _windowState;
    #endregion
}
