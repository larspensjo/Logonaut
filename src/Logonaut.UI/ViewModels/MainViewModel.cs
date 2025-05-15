using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
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
    private readonly IScheduler? _backgroundScheduler; // Make it possible to inject your own background scheduler
    private readonly ILogSourceProvider _sourceProvider;
    public static readonly object LoadingToken = new();
    public static readonly object FilteringToken = new();
    public static readonly object BurstToken = new();
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsService _settingsService;

    private readonly SynchronizationContext _uiContext;
    private IReactiveFilteredLogStream _reactiveFilteredLogStream; // This will be recreated when switching sources

    private readonly HashSet<int> _existingOriginalLineNumbers = new HashSet<int>(); // A helper HashSet to efficiently track existing original line numbers

    // --- Lifecycle Management ---
    private readonly CompositeDisposable _disposables = new();
    private IDisposable? _filterSubscription;
    private IDisposable? _totalLinesSubscription;
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

    const int MaxPaletteDisplayTextLength = 20; // Or whatever fits your UI

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

    // Collection of filter patterns (substrings/regex) for highlighting.
    // Note: This state is derived by traversing the *active* FilterProfile.
    [ObservableProperty] private ObservableCollection<string> _filterSubstrings = new();

    // Observable collection holding tokens representing active background tasks.
    // Bound to the UI's BusyIndicator; the indicator spins if this collection is not empty.
    // Add/remove specific tokens (e.g., LoadingToken) to control the busy state.
    public ObservableCollection<object> CurrentBusyStates { get; } = new();
    public bool IsLoading => CurrentBusyStates.Contains(LoadingToken) || CurrentBusyStates.Contains(BurstToken);

    private void LoadPersistedSettings()
    {
        LogonautSettings settings = _settingsService.LoadSettings();

        // --- Load Filter Profiles FIRST ---
        // This ensures ActiveFilterProfile is set before any saves are triggered.
        LoadFilterProfiles(settings);

        // --- Load Display/Search Settings ---
        // Setting these might trigger saves, which is now safe.
        _lastOpenedFolderPath = settings.LastOpenedFolderPath;

        LoadUiSettings(settings);
        LoadSimulatorPersistedSettings(settings);
    }

    private void SaveCurrentSettingsDelayed() => _uiContext.Post(_ => SaveCurrentSettings(), null);

    private void SaveCurrentSettings()
    {
        var settingsToSave = new LogonautSettings
        {
            // --- Save Display/Search Settings ---
            LastOpenedFolderPath = this._lastOpenedFolderPath,
        };

        SaveUiSettings(settingsToSave);
        SaveSimulatorSettings(settingsToSave);

        // --- Save Filter Profiles ---
        SaveFilterProfiles(settingsToSave); // Handles LastActiveProfileName and the list

        // --- Save to Service ---
        _settingsService.SaveSettings(settingsToSave);
    }
    #endregion // --- UI State Management ---

    #region Command Handling

    private bool CanPerformActionWhileNotLoading()
    {
        // Check if the loading token is NOT present
        return !CurrentBusyStates.Contains(LoadingToken);
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

    #endregion // --- Command Handling ---

    #region Orchestration & Updates

    private void AddLineToLogDocument(string line) => LogDoc.AppendLine(line);

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

    private void HandleProcessorError(string contextMessage, Exception ex)
    {
        Debug.WriteLine($"{contextMessage}: {ex}");
        _uiContext.Post(_ =>
        {
            CurrentBusyStates.Clear();
            MessageBox.Show($"Error processing logs: {ex.Message}", "Processing Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }, null);
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
        CurrentActiveLogSource?.StopMonitoring(); // Explicitly stop monitoring before disposing
        Dispose(); // Calls the main Dispose method which cleans everything else
    }
    #endregion // --- Lifecycle Management ---
}
