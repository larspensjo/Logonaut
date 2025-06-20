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
     * Core logic for loading a log file. It checks if a tab for the file already exists.
     * If so, it activates that tab. If not, it creates a new tab for the file,
     * adds it to the tab collection, and activates it.
     */
    public async Task LoadLogFileCoreAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Called with null or empty filePath. Aborting.");
            return;
        }

        // Step 1.2: Check if a tab for this file already exists.
        var existingTab = TabViewModels.FirstOrDefault(t => t.SourceType == SourceType.File && t.SourceIdentifier == filePath);
        if (existingTab != null)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Found existing tab for '{filePath}'. Activating it.");
            ActiveTabViewModel = existingTab;
            return;
        }

        // Step 1.2: If not, create a new tab.
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Creating new tab for '{filePath}'.");

        var newTab = new TabViewModel(
            initialHeader: Path.GetFileName(filePath),
            initialAssociatedProfileName: ActiveFilterProfile?.Name ?? "Default", // Use current profile
            initialSourceType: SourceType.File,
            initialSourceIdentifier: filePath,
            _sourceProvider,
            this, // ICommandExecutor
            _uiContext,
            _backgroundScheduler
        );

        // This method handles adding to collection and subscribing to events
        AddTab(newTab);
        ActiveTabViewModel = newTab; // Set as active

        try
        {
            // ActivateAsync will handle displaying the busy indicator on the tab itself
            await newTab.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled,
                null
            );

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} LoadLogFileCoreAsync: New tab activated for '{filePath}'.");

            _lastOpenedFolderPath = Path.GetDirectoryName(filePath);
            MarkSettingsAsDirty(); // LastOpenedFolderPath changed
            CurrentGlobalLogFilePathDisplay = filePath;
        }
        catch (Exception ex)
        {
            // If activation fails, we should close the newly created tab and show an error.
            _uiContext.Post(_ =>
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.LoadLogFileCoreAsync: Error opening file '{filePath}': {ex.Message}");
                MessageBox.Show($"Error opening or reading log file '{filePath}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentGlobalLogFilePathDisplay = null; // Clear display path
                CloseTab(newTab); // Clean up the failed tab
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
