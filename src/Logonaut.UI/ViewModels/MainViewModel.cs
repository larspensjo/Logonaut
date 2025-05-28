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
using Logonaut.UI.Commands;
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

    // The single, implicit tab for Phase 0.1
    private readonly TabViewModel _internalTabViewModel;

    public ThemeViewModel Theme { get; }

    private bool _settingsDirty = false; // Flag to track if settings need saving

    #endregion

    #region Stats Properties (Now Delegated or Global)

    // These now delegate to the _internalTabViewModel for Phase 0.1
    public long TotalLogLines => _internalTabViewModel.TotalLogLines;
    public int FilteredLogLinesCount => _internalTabViewModel.FilteredLogLinesCount;

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

        // STEP 1: Initialize _internalTabViewModel FIRST
        // It needs a default AssociatedFilterProfileName. We can't get it from ActiveFilterProfile yet,
        // so use a sensible default like "Default". It will be updated shortly by OnActiveFilterProfileChanged.
        _internalTabViewModel = new TabViewModel(
            initialHeader: "Main Log",
            initialAssociatedProfileName: "Default",
            initialSourceType: SourceType.Pasted,
            initialSourceIdentifier: null,
            _sourceProvider,
            this,
            _uiContext,
            _backgroundScheduler
        );
        _disposables.Add(_internalTabViewModel);

        _internalTabViewModel.RequestScrollToEnd += (s, e) => RequestGlobalScrollToEnd?.Invoke(s, e);
        _internalTabViewModel.RequestScrollToLineIndex += (s, e) => RequestGlobalScrollToLineIndex?.Invoke(s, e);
        _internalTabViewModel.PropertyChanged += InternalTabViewModel_PropertyChanged;
        _internalTabViewModel.FilteredLinesUpdated += InternalTabViewModel_FilteredLinesUpdated;
        _disposables.Add(Disposable.Create(() =>
            {
                _internalTabViewModel.PropertyChanged -= InternalTabViewModel_PropertyChanged;
                _internalTabViewModel.FilteredLinesUpdated -= InternalTabViewModel_FilteredLinesUpdated;
            }));

        // STEP 2: Now load settings, which will set ActiveFilterProfile and trigger its OnChanged ---
        // OnActiveFilterProfileChanged will then correctly update _internalTabViewModel.AssociatedFilterProfileName
        LoadPersistedSettings();

        // STEP 3: "Activate" the internal tab
        // Now that ActiveFilterProfile is set, _internalTabViewModel's AssociatedFilterProfileName
        // should have been correctly updated by OnActiveFilterProfileChanged.
        // Its ActivateAsync will use its (now correct) AssociatedFilterProfileName.
        _ = _internalTabViewModel.ActivateAsync(
            this.AvailableProfiles,
            this.ContextLines,
            this.HighlightTimestamps,
            this.ShowLineNumbers,
            this.IsAutoScrollEnabled,
            null
            ).ContinueWith(t => {
                if (t.IsFaulted) Debug.WriteLine($"Error activating internal tab: {t.Exception}");
            });

        PopulateFilterPalette(); // Remains in MainViewModel as palette is global

        ToggleAboutOverlayCommand = new RelayCommand(ExecuteToggleAboutOverlay);
        LoadRevisionHistory();
    }

    #endregion

    #region About command
    [ObservableProperty] private bool _isAboutOverlayVisible;
    [ObservableProperty] private string? _aboutRevisionHistory;
    public string ApplicationVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
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

    private void InternalTabViewModel_FilteredLinesUpdated(object? sender, EventArgs e) // <<< NEW HANDLER
    {
        if (sender == _internalTabViewModel)
        {
            Debug.WriteLine($"---> MainViewModel: Received FilteredLinesUpdated from Tab. Calling UpdateActiveFilterMatchingStatus.");
            _uiContext.Post(_ => UpdateActiveFilterMatchingStatus(), null); // Ensure UI thread for safety
        }
    }

    private void InternalTabViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender != _internalTabViewModel) return;

        // Forward relevant property changes from TabViewModel to MainViewModel's listeners
        switch (e.PropertyName)
        {
            case nameof(TabViewModel.IsLoading):
                // The tab's loading state changed. This affects CanPerformActionWhileNotLoading.
                // Re-evaluate CanExecute for commands that use it.
                _uiContext.Post(_ =>
                {
                    (OpenLogFileCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
                    ToggleSimulatorCommand.NotifyCanExecuteChanged();
                    (RestartSimulatorCommand as IRelayCommand)?.NotifyCanExecuteChanged();
                    (ClearLogCommand as IRelayCommand)?.NotifyCanExecuteChanged();
                    // GenerateBurstCommand's CanExecute is different (CanGenerateBurst),
                    // but if Simulator becomes active/inactive, IsSimulatorRunning changes,
                    // which should trigger GenerateBurstCommand.NotifyCanExecuteChanged() via NotifySimulatorCommandsCanExecuteChanged().
                    // However, to be safe, or if CanGenerateBurst also checks general loading:
                    (GenerateBurstCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
                }, null);
                break;
            case nameof(TabViewModel.TotalLogLines):
                OnPropertyChanged(nameof(TotalLogLines));
                break;
            case nameof(TabViewModel.FilteredLogLinesCount):
                OnPropertyChanged(nameof(FilteredLogLinesCount));
                break;
            case nameof(TabViewModel.SearchText):
                OnPropertyChanged(nameof(SearchText));
                break;
            case nameof(TabViewModel.HighlightedOriginalLineNumber):
                OnPropertyChanged(nameof(HighlightedOriginalLineNumber));
                break;
            case nameof(TabViewModel.HighlightedFilteredLineIndex):
                OnPropertyChanged(nameof(HighlightedFilteredLineIndex));
                break;
            case nameof(TabViewModel.TargetOriginalLineNumberInput):
                OnPropertyChanged(nameof(TargetOriginalLineNumberInput));
                break;
            case nameof(TabViewModel.JumpStatusMessage):
                OnPropertyChanged(nameof(JumpStatusMessage));
                break;
            case nameof(TabViewModel.IsJumpTargetInvalid):
                OnPropertyChanged(nameof(IsJumpTargetInvalid));
                break;
            case nameof(TabViewModel.FilterHighlightModels):
                OnPropertyChanged(nameof(FilterHighlightModels));
                break;
                // Add other properties as needed
        }
    }
    #region Delegated Properties and Commands for UI Binding (Phase 0.1)

    // These properties allow existing UI bindings to MainViewModel to continue working by delegating to _internalTabViewModel.
    // In Phase 1, UI bindings will change to MainViewModel.ActiveTab.Property.

    public ObservableCollection<FilteredLogLine> FilteredLogLines => _internalTabViewModel.FilteredLogLines;
    public ObservableCollection<IFilter> FilterHighlightModels => _internalTabViewModel.FilterHighlightModels; // For AvalonEdit

    public int HighlightedFilteredLineIndex
    {
        get => _internalTabViewModel.HighlightedFilteredLineIndex;
        set => _internalTabViewModel.HighlightedFilteredLineIndex = value;
    }
    public int HighlightedOriginalLineNumber
    {
        get => _internalTabViewModel.HighlightedOriginalLineNumber;
        set => _internalTabViewModel.HighlightedOriginalLineNumber = value;
    }

    public string TargetOriginalLineNumberInput
    {
        get => _internalTabViewModel.TargetOriginalLineNumberInput;
        set => _internalTabViewModel.TargetOriginalLineNumberInput = value;
    }
    public string? JumpStatusMessage
    {
        get => _internalTabViewModel.JumpStatusMessage;
        // set => _internalTabViewModel.JumpStatusMessage = value; // Usually read-only from VM side
    }
    public bool IsJumpTargetInvalid
    {
        get => _internalTabViewModel.IsJumpTargetInvalid;
        // set => _internalTabViewModel.IsJumpTargetInvalid = value; // Usually read-only from VM side
    }
    public IRelayCommand JumpToLineCommand => _internalTabViewModel.JumpToLineCommand;


    public string SearchText
    {
        get => _internalTabViewModel.SearchText;
        set => _internalTabViewModel.SearchText = value;
    }
    public bool IsCaseSensitiveSearch // This UI setting is global, but applies to the tab's search
    {
        get => _internalTabViewModel.IsCaseSensitiveSearch;
        set
        {
            if (_internalTabViewModel.IsCaseSensitiveSearch != value)
            {
                _internalTabViewModel.IsCaseSensitiveSearch = value;
                OnPropertyChanged(); // Notify UI if MainViewModel has direct binding
                MarkSettingsAsDirty(); // Global setting change
            }
        }
    }
    public ObservableCollection<SearchResult> SearchMarkers => _internalTabViewModel.SearchMarkers;
    public int CurrentMatchOffset => _internalTabViewModel.CurrentMatchOffset;
    public int CurrentMatchLength => _internalTabViewModel.CurrentMatchLength;
    public string SearchStatusText => _internalTabViewModel.SearchStatusText;
    public IRelayCommand PreviousSearchCommand => _internalTabViewModel.PreviousSearchCommand;
    public IRelayCommand NextSearchCommand => _internalTabViewModel.NextSearchCommand;

    // For BusyIndicator specific to the tab's loading/filtering
    public ObservableCollection<object> CurrentBusyStates => _internalTabViewModel.CurrentBusyStates;
    public bool IsLoading => _internalTabViewModel.IsLoading;


    // Editor instance management
    private TextEditor? _logEditorInstance; // MainViewModel still holds this, but passes to TabViewModel
    public void SetLogEditorInstance(TextEditor editor)
    {
        _logEditorInstance = editor;
        _internalTabViewModel.SetLogEditorInstance(editor);
    }

    // CurrentLogFilePath is a bit special. MainViewModel still shows it globally.
    // TabViewModel.SourceIdentifier will hold the actual path for its source.
    [ObservableProperty] private string? _currentGlobalLogFilePathDisplay;


    #endregion


    private void LoadPersistedSettings()
    {
        // Add GlobalLoadingToken:
        _uiContext.Post(_ => CurrentGlobalBusyStates.Add(GlobalLoadingToken), null);

        LogonautSettings settings = _settingsService.LoadSettings();
        Debug.WriteLine($"---> MainViewModel: Loaded settings from {settings.WindowWidth}");
        LoadFilterProfiles(settings); // This sets ActiveFilterProfile

        // After profiles are loaded and ActiveFilterProfile is known,
        // update the _internalTabViewModel's AssociatedFilterProfileName
        if (ActiveFilterProfile != null)
        {
            _internalTabViewModel.AssociatedFilterProfileName = ActiveFilterProfile.Name;
        }

        _lastOpenedFolderPath = settings.LastOpenedFolderPath;
        LoadUiSettings(settings); // Loads ContextLines, ShowLineNumbers etc.
        LoadSimulatorPersistedSettings(settings);

        // Load window geometry and filter panel width into MainViewModel properties
        WindowTop = settings.WindowTop;
        WindowLeft = settings.WindowLeft;
        WindowHeight = settings.WindowHeight;
        WindowWidth = settings.WindowWidth;
        FilterPanelWidth = settings.FilterPanelWidth;
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
    private void SaveCurrentSettings()
    {
        Debug.WriteLine("---> MainViewModel: SaveCurrentSettings called.");
        var settingsToSave = new LogonautSettings
        {
            LastOpenedFolderPath = this._lastOpenedFolderPath,
            // Window geometry will be populated by MainWindow before this is called on exit,
            // or we need properties in MainViewModel for MainWindow to update.
            // For now, assuming MainWindow updates its part directly or via properties here.
        };

        // Populate window geometry from MainViewModel properties (which MainWindow will update)
        settingsToSave.WindowTop = WindowTop;
        settingsToSave.WindowLeft = WindowLeft;
        settingsToSave.WindowHeight = WindowHeight;
        settingsToSave.WindowWidth = WindowWidth;
        settingsToSave.FilterPanelWidth = FilterPanelWidth;

        SaveUiSettings(settingsToSave);
        SaveSimulatorSettings(settingsToSave);
        SaveFilterProfiles(settingsToSave);
        _settingsService.SaveSettings(settingsToSave);
        _settingsDirty = false; // Reset dirty flag after saving
        Debug.WriteLine("---> MainViewModel: Settings saved successfully.");
    }

    private bool CanPerformActionWhileNotLoading()
    {
        bool tabIsLoading = _internalTabViewModel.IsLoading; // Call the getter
        bool mainIsLoading = IsGloballyLoading;
        bool canPerform = !mainIsLoading && !tabIsLoading;
        Debug.WriteLine($"---> MainViewModel.CanPerformActionWhileNotLoading: Result={canPerform}. MainLoading={mainIsLoading}, TabLoading={tabIsLoading}");
        return canPerform;
    }

    #region Orchestration & Updates (Altered)

    private void TriggerFilterUpdate()
    {
        IFilter? filterToApply = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();

        // Update FilterHighlightModels on the internal tab
        var newFilterModels = new ObservableCollection<IFilter>();
        if (ActiveFilterProfile?.RootFilterViewModel != null)
        {
            TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterModels);
        }
        _internalTabViewModel.FilterHighlightModels = newFilterModels; // Pass to tab

        // Tell the internal tab to update its filters (and thus its FilteredLogLines)
        _internalTabViewModel.ApplyFiltersFromProfile(this.AvailableProfiles, this.ContextLines);

        // UpdateActiveFilterMatchingStatus uses _internalTabViewModel.FilteredLogLines.
        // This call might be slightly premature if ApplyFiltersFromProfile results in async updates to FilteredLogLines.
        // However, ReactiveFilteredLogStream typically produces a ReplaceFilteredUpdate fairly quickly after settings change.
        UpdateActiveFilterMatchingStatus();
    }

    // GetCurrentDocumentText for MainViewModel (e.g., for filter matching status)
    // should now get it from the internal tab's perspective.
    private string GetCurrentDocumentText()
    {
        // In Phase 0.1, the editor instance is still managed by MainWindow and passed down.
        // TabViewModel will have its own GetCurrentDocumentTextForSearch.
        // This one is for MainViewModel's direct needs if any, like UpdateActiveFilterMatchingStatus.
        if (_internalTabViewModel.FilteredLogLines.Any())
        {
            // This mirrors TabViewModel's fallback if its editor isn't set.
            return string.Join(Environment.NewLine, _internalTabViewModel.FilteredLogLines.Select(fll => fll.Text));
        }
        return string.Empty;
    }

    // ApplyFilteredUpdate is now largely handled by TabViewModel.
    // MainViewModel's role here diminishes significantly for Phase 0.1.
    // The subscriptions in MainViewModel's constructor to _reactiveFilteredLogStream are removed.
    // TabViewModel will have its own subscriptions.

    #endregion

    #region Lifecycle Management ---
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activeProfileNameSubscription?.Dispose();
            _disposables.Dispose(); // Disposes _internalTabViewModel
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
    // These properties will be updated by MainWindow when its geometry changes.
    // MainViewModel will then use these values when SaveCurrentSettings is called.
    // We don't use [ObservableProperty] if MainViewModel itself doesn't need to notify its own UI for these.
    // However, if any UI *within MainViewModel's scope* (not MainWindow's directly bound elements)
    // depended on these, then [ObservableProperty] would be needed. For now, assume not.
    // Let's make them observable in case some debug UI or future feature in MainViewModel needs them.
    [ObservableProperty] private double _windowTop;
    [ObservableProperty] private double _windowLeft;
    [ObservableProperty] private double _windowHeight;
    [ObservableProperty] private double _windowWidth;
    [ObservableProperty] private double _filterPanelWidth;
    #endregion
}
