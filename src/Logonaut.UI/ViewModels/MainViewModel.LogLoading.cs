using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.Core.Commands;
using System.IO; 

namespace Logonaut.UI.ViewModels;

/*
 * Partial class for MainViewModel responsible for orchestrating log loading operations.
 * This includes opening log files and handling pasted log content. In the tabbed interface,
 * these operations create and manage new or existing tabs.
 */
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    private string? _lastOpenedFolderPath;

    /*
     * Handles the "Open Log File" command. Prompts the user to select a log file,
     * then calls LoadLogFileCoreAsync to load the content into a new or existing tab.
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

    /*
     * Core logic for loading a log file from a given path.
     * In Step 1.1, this method reconfigures the *active* TabViewModel to handle the file.
     * In Step 1.2, this will be updated to create a new tab instead.
     */
    public async Task LoadLogFileCoreAsync(string filePath)
    {
        if (ActiveTabViewModel == null)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: No active tab to load into. Aborting.");
            return;
        }

        if (ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim && sim.IsRunning)
        {
            if (ActiveTabViewModel.SourceType == SourceType.Simulator)
            {
                (ActiveTabViewModel.LogSourceExposeDeprecated as ISimulatorLogSource)?.Stop();
            }
        }

        if (string.IsNullOrEmpty(filePath))
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Called with null or empty filePath. Aborting.");
            return;
        }

        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Loading '{filePath}' into active tab '{ActiveTabViewModel.Header}'");
        _uiContext.Post(_ => ActiveTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null);
        CurrentGlobalLogFilePathDisplay = filePath;

        try
        {
            ActiveTabViewModel.DeactivateLogProcessing();

            // Reconfigure the active tab for the new file
            ActiveTabViewModel.Header = Path.GetFileName(filePath);
            ActiveTabViewModel.SourceType = SourceType.File;
            ActiveTabViewModel.SourceIdentifier = filePath;

            await ActiveTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled,
                null
            );

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LoadLogFileCoreAsync: Active tab activated for '{filePath}'.");

            _lastOpenedFolderPath = Path.GetDirectoryName(filePath);
            MarkSettingsAsDirty(); // LastOpenedFolderPath changed
        }
        catch (Exception ex)
        {
            _uiContext.Post(_ =>
            {
                ActiveTabViewModel?.CurrentBusyStates.Remove(TabViewModel.LoadingToken);
                ActiveTabViewModel?.CurrentBusyStates.Remove(TabViewModel.FilteringToken);

                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Error opening file '{filePath}': {ex.Message}");
                MessageBox.Show($"Error opening or reading log file '{filePath}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentGlobalLogFilePathDisplay = null;
            }, null);
        }
    }

    /*
     * Loads log content directly from a text string by creating a new tab.
     * This follows the plan for Step 2.3.
     */
    public void LoadLogFromText(string text)
    {
        // 1. Create a new TabViewModel instance for the pasted content.
        var newTab = new TabViewModel(
            initialHeader: "[Pasted Content]",
            initialAssociatedProfileName: ActiveFilterProfile?.Name ?? "Default", // Use current profile
            initialSourceType: SourceType.Pasted,
            initialSourceIdentifier: $"pasted_{Guid.NewGuid()}",
            _sourceProvider,
            this, // ICommandExecutor
            _uiContext,
            _backgroundScheduler
        );

        // 2. Add the new tab to the collection and make it active.
        AddTab(newTab);
        ActiveTabViewModel = newTab;

        // 3. Load the pasted text into the new tab's processor.
        newTab.LoadPastedContent(text);

        // 4. Explicitly activate the new tab to apply filters.
        _ = newTab.ActivateAsync(
           this.AvailableProfiles,
           this.ContextLines,
           this.HighlightTimestamps,
           this.ShowLineNumbers,
           this.IsAutoScrollEnabled,
           null
       ).ContinueWith(t =>
       {
           if (t.IsFaulted && t.Exception != null)
           {
               Debug.WriteLine($"Error activating new pasted tab: {t.Exception.Flatten().Message}");
           }
       });

        CurrentGlobalLogFilePathDisplay = newTab.Header;
    }

    /*
     * Resets the currently active tab data, clearing its log document and filtered view.
     */
    private void ResetCurrentlyActiveTabData()
    {
        if (ActiveTabViewModel == null) return;

        ActiveTabViewModel.DeactivateLogProcessing();

        if (ActiveTabViewModel.SourceType == SourceType.Pasted)
        {
            ActiveTabViewModel.LoadPastedContent(string.Empty);
        }

        _ = ActiveTabViewModel.ActivateAsync(
           this.AvailableProfiles,
           this.ContextLines,
           this.HighlightTimestamps,
           this.ShowLineNumbers,
           this.IsAutoScrollEnabled,
           null
       ).ContinueWith(t =>
       {
           if (t.IsFaulted && t.Exception != null) Debug.WriteLine($"Error reactivating tab after reset: {t.Exception.Flatten().Message}");
       });

        ActiveTabViewModel.Header = "[Cleared]";
        CurrentGlobalLogFilePathDisplay = ActiveTabViewModel.Header;
    }
}
