using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Commands;
using System.IO; 

namespace Logonaut.UI.ViewModels;

/*
 * Partial class for MainViewModel responsible for orchestrating log loading operations.
 * This includes opening log files and handling pasted log content, primarily by
 * configuring and activating the internal TabViewModel to manage the data.
 */
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    private string? _lastOpenedFolderPath;

    /*
     * Handles the "Open Log File" command. Prompts the user to select a log file,
     * then calls LoadLogFileCoreAsync to load and display the content from the specified file.
     */
    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private async Task OpenLogFileAsync()
    {
        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*", _lastOpenedFolderPath);
        if (string.IsNullOrEmpty(selectedFile))
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.OpenLogFileAsync: File selection cancelled by user.");
            return;
        }
        
        await LoadLogFileCoreAsync(selectedFile);
    }

    // A callback that asks us to restart the same log file.
    private void OnLogfileRestart()
    {
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.OnLogfileRestart: invoked.");
        _uiContext.Post(_ =>
        {
            // The callback comes from another thread, so we need to use the _uicontext.
            _ = LoadLogFileCoreAsync(CurrentGlobalLogFilePathDisplay!);
        }, null);
    }

    /*
     * Core logic for loading a log file from a given path.
     * This method reconfigures and activates the internal TabViewModel to handle the file.
     * It updates UI elements related to the file path and manages busy states.
     * 
     * This method can be called directly if the file path is already known,
     * bypassing the user file selection dialog.
     */
    public async Task LoadLogFileCoreAsync(string filePath)
    {
        if (_internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim && sim.IsRunning)
        {
            StopSimulatorInInternalTab();
        }

        if (string.IsNullOrEmpty(filePath))
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Called with null or empty filePath. Aborting.");
            // Optionally, display an error message or throw an ArgumentNullException
            // For now, just logging and returning to match potential implicit behavior.
            return;
        }

        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Loading '{filePath}'");
        _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null);
        CurrentGlobalLogFilePathDisplay = filePath;

        try
        {
            _internalTabViewModel.DeactivateLogProcessing();

            // Reconfigure the internal tab for the new file
            _internalTabViewModel.Header = Path.GetFileName(filePath);
            _internalTabViewModel.SourceType = SourceType.File;
            _internalTabViewModel.SourceIdentifier = filePath;

            await _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled,
                OnLogfileRestart
            );

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LoadLogFileCoreAsync: Tab activated for '{filePath}'.");

            _lastOpenedFolderPath = Path.GetDirectoryName(filePath);
            MarkSettingsAsDirty(); // LastOpenedFolderPath changed
        }
        catch (Exception ex)
        {
            // TabViewModel.ActivateAsync or its LogDataProcessor should handle their own busy token removal on error.
            // MainViewModel catches to display a message and reset its own state if needed.
            _uiContext.Post(_ =>
            {
                _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.LoadingToken);
                _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.FilteringToken);

                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Error opening file '{filePath}': {ex.Message}");
                MessageBox.Show($"Error opening or reading log file '{filePath}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentGlobalLogFilePathDisplay = null;
            }, null);
            // Consider re-throwing or returning a status if external callers need to know about the failure
        }
        // Note: The TabViewModel.LoadingToken is removed by the TabViewModel itself upon successful
        // completion of its activation and initial filtering (via FilteredUpdate with IsInitialLoadProcessingComplete = true),
        // or by the catch block here in case of an error during this core loading phase.
    }

    /*
     * Loads log content directly from a text string, typically from a paste operation.
     * It configures the internal TabViewModel to handle this pasted data, updates
     * its content, and then activates it to apply filters and display the log.
     */
    public void LoadLogFromText(string text)
    {
        if (_internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim && sim.IsRunning)
        {
            StopSimulatorInInternalTab();
        }

        _internalTabViewModel.DeactivateLogProcessing();
        _internalTabViewModel.SourceType = SourceType.Pasted;
        _internalTabViewModel.SourceIdentifier = null;
        _internalTabViewModel.Header = "[Pasted Content]";
        CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header;

        _internalTabViewModel.LoadPastedContent(text);

        // Activate the tab to process the pasted content with filters
        _ = _internalTabViewModel.ActivateAsync(
           this.AvailableProfiles,
           this.ContextLines,
           this.HighlightTimestamps,
           this.ShowLineNumbers,
           this.IsAutoScrollEnabled,
           null
       ).ContinueWith(t =>
       {
           if (t.IsFaulted && t.Exception != null) Debug.WriteLine($"Error activating internal tab after paste: {t.Exception.Flatten().Message}");
       });
    }

    /*
     * Resets the currently active tab data, clearing its log document and filtered view.
     * It effectively deactivates and then reactivates the internal TabViewModel to
     * process an empty log document, resulting in a cleared display.
     */
    private void ResetCurrentlyActiveTabData()
    {
        _internalTabViewModel.DeactivateLogProcessing();

        // TabViewModel's ActivateAsync (for File/Simulator) will handle clearing its processor's LogDoc
        // and its own UI collections. For a generic reset, we might need a more explicit clear.
        // For now, Deactivate and then Activate with a potentially implied empty source will reset it.
        // If the SourceType was File/Simulator, Activate will clear the doc.
        // If it was Pasted, LogDoc in processor might still hold old data unless explicitly cleared.
        // Let's ensure pasted content is also cleared.
        if (_internalTabViewModel.SourceType == SourceType.Pasted)
        {
            _internalTabViewModel.LoadPastedContent(string.Empty); // Clears processor's LogDoc and UI collections
        }
        // For File/Simulator, ActivateAsync will handle the LogDoc clear in the processor.
        // We still need to ensure UI collections are reset if ActivateAsync isn't robust enough.
        // TabViewModel.ResetUICollectionsAndState() should be called within its ActivateAsync if needed for File/Sim.
        // The current TabViewModel.ActivateAsync does call ResetUICollectionsAndState for File/Sim.

        _ = _internalTabViewModel.ActivateAsync(
           this.AvailableProfiles,
           this.ContextLines,
           this.HighlightTimestamps,
           this.ShowLineNumbers,
           this.IsAutoScrollEnabled,
           null
       ).ContinueWith(t =>
       {
           if (t.IsFaulted && t.Exception != null) Debug.WriteLine($"Error reactivating internal tab after reset: {t.Exception.Flatten().Message}");
       });

        _internalTabViewModel.Header = "[Cleared]";
        CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header;
    }
}
