using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Windows; // For Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Commands;

namespace Logonaut.UI.ViewModels;

// This groups all functionality related to acquiring log data from different sources (files, pasted text,
// and indirectly, the simulator when it becomes the active source).
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    private string? _lastOpenedFolderPath;
    private ILogSource? _fileLogSource; // Keep a reference to the file source instance

    // Path of the currently monitored log file.
    [ObservableProperty] private string? _currentLogFilePath;
    [ObservableProperty] private ILogSource _currentActiveLogSource;

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

    // Responsible for reacting to the switch of CurrentActiveLogSource
    partial void OnCurrentActiveLogSourceChanged(ILogSource? oldValue, ILogSource newValue)
    {
        Debug.WriteLine($"---> CurrentActiveLogSource changed from {oldValue?.GetType().Name ?? "null"} to {newValue.GetType().Name}");


        // 1. Update CanExecute for GenerateBurstCommand
        GenerateBurstCommand.NotifyCanExecuteChanged();
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

    private void ProcessTotalLinesUpdate(long count)
    {
        TotalLogLines = count;
    }
}
