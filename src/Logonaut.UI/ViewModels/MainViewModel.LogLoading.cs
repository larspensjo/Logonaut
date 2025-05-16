using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.Commands;
using System.IO; // Required for Path

namespace Logonaut.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    private string? _lastOpenedFolderPath;
    
    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private async Task OpenLogFileAsync()
    {
        if (_internalTabViewModel.LogSource is ISimulatorLogSource sim && sim.IsRunning)
        {
            StopSimulatorInInternalTab(); // Stop global simulator if running
        }

        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*", _lastOpenedFolderPath);
        if (string.IsNullOrEmpty(selectedFile)) return;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.OpenLogFileAsync: '{selectedFile}'");

        // Use internal tab's busy state
        _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null);
        CurrentGlobalLogFilePathDisplay = selectedFile; // Update global display

        try
        {
            // Deactivate first to clean up existing source/stream, if any
            _internalTabViewModel.Deactivate(); 
            
            // Reconfigure the internal tab for the new file
            _internalTabViewModel.Header = Path.GetFileName(selectedFile);
            // _internalTabViewModel.SourceType is implicitly set by how ActivateAsync works or needs explicit setter if we add one.
            // For now, ActivateAsync will determine source type based on how it's called or its existing SourceIdentifier
            // Let's assume for file loading, we need to ensure its SourceType and SourceIdentifier are set correctly before activation.
            // This part needs careful design for TabViewModel's reconfigurability.
            // For Phase 0.1, we might be "recreating" the LogSource within TabViewModel upon activation based on a new SourceIdentifier.
            // The plan for TabViewModel.ActivateAsync: "Creates/recreates ILogSource... based on SourceType and SourceIdentifier"
            // So, we update SourceIdentifier here.
            
            // A more explicit way if TabViewModel had a method like `ReconfigureForFile(string path)`:
            // await _internalTabViewModel.ReconfigureForFile(selectedFile, ActiveFilterProfile.Name);

            // For now, directly modify TabViewModel state then activate:
            // This is a simplified approach for Phase 0.1 with a single internal tab.
            // In a multi-tab scenario, we'd create a new TabViewModel or find existing.
            // For this phase, we are re-purposing the single _internalTabViewModel.
            // Effectively, we are changing its "identity" to the new file.
            
            // Manually set SourceType and SourceIdentifier before ActivateAsync.
            // This is a bit of a hack for Phase 0.1. Ideally, TabViewModel would have a specific LoadFile method.
            // For now, we modify its state and rely on ActivateAsync to pick it up.
            // This assumes TabViewModel's constructor logic for SourceType is not re-run.
            // This is okay because we are not creating a new TabViewModel instance.
            
            // This requires _internalTabViewModel to allow changing SourceIdentifier and SourceType after construction or using a specific load method.
            // The plan: "TabViewModel constructor should accept: ... SourceType initialSourceType, string? initialSourceIdentifier"
            // This implies they are set at construction. If we re-purpose the single _internalTabViewModel,
            // we need a way to tell it "you are now a file tab for this file".
            
            // Let's simulate creating a "new" conceptual source for the _internalTabViewModel
            _internalTabViewModel.Deactivate(); // Ensure it's fully stopped
            // Reset its LogDoc and other states as if it's a new file load
            _internalTabViewModel.LogDoc.Clear(); 
            _internalTabViewModel.FilteredLogLines.Clear();
            // _internalTabViewModel.TotalLogLines = 0; // This is updated by stream
            _internalTabViewModel.SourceIdentifier = selectedFile; 
            // We need to ensure TabViewModel uses File source type on next activation
            // This is problematic with current TabViewModel constructor.
            // For Phase 0.1, the _internalTabViewModel is fixed as one type at construction.
            // OpenLogFileAsync should operate on a TabViewModel of SourceType.File.
            // If _internalTabViewModel is not SourceType.File, this logic is flawed for Phase 0.1.

            // *** Re-evaluation for Phase 0.1: MainViewModel orchestrates _internalTabViewModel ***
            // _internalTabViewModel will already have its LogSource and Stream from its initial activation.
            // When opening a new file, we need to:
            // 1. Deactivate the current LogSource/Stream in _internalTabViewModel.
            // 2. Re-initialize _internalTabViewModel's LogSource to a new FileLogSource.
            // 3. Call _internalTabViewModel's ActivateAsync (or a part of it) to load the new file.

            // The plan: "TabViewModel.ActivateAsync(): Creates/recreates ILogSource... based on SourceType and SourceIdentifier"
            // So, we update SourceIdentifier on _internalTabViewModel and ensure its SourceType is 'File'
            // (which it should be if OpenLogFile is called).
            // If _internalTabViewModel was, say, a Simulator, this needs careful handling.
            // For Phase 0.1, let's assume OpenLogFileAsync MEANS the _internalTabViewModel BECOMES a File tab.

            // This will force _internalTabViewModel.ActivateAsync to create a FileLogSource
            // This is a conceptual change of the single tab's nature.
            // A cleaner way would be:
            // _internalTabViewModel.LoadAsFileSource(selectedFile, ...);
            // For now, we set properties and rely on ActivateAsync.
            typeof(TabViewModel).GetProperty("SourceType")!.SetValue(_internalTabViewModel, SourceType.File); // Hacky, need better way or method
            _internalTabViewModel.SourceIdentifier = selectedFile;


            await _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles, 
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled
            );
            // TotalLogLines is updated by _internalTabViewModel's stream subscription.

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Tab activated for '{selectedFile}'.");

            _lastOpenedFolderPath = System.IO.Path.GetDirectoryName(selectedFile);
            SaveCurrentSettingsDelayed();
        }
        catch (Exception ex)
        {
            _uiContext.Post(_ =>
            {
                _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.LoadingToken);
                _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.FilteringToken);
                // Tell tab to reset its state if error
                _internalTabViewModel.LogDoc.Clear(); 
                _internalTabViewModel.FilteredLogLines.Clear();
                // Consider calling a more comprehensive reset on _internalTabViewModel
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Error opening file '{selectedFile}': {ex.Message}");
                MessageBox.Show($"Error opening or reading log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentGlobalLogFilePathDisplay = null;
                _internalTabViewModel.LogSource?.StopMonitoring();
            }, null);
        }
        finally
        {
            // This should be handled by TabViewModel.ActivateAsync or its error path.
            // _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.LoadingToken), null);
        }
    }

    public void LoadLogFromText(string text)
    {
        if (_internalTabViewModel.LogSource is ISimulatorLogSource sim && sim.IsRunning)
        {
            StopSimulatorInInternalTab();
        }
        CurrentGlobalLogFilePathDisplay = "[Pasted Content]";

        // Reconfigure internal tab for pasted content
        _internalTabViewModel.Deactivate();
        typeof(TabViewModel).GetProperty("SourceType")!.SetValue(_internalTabViewModel, SourceType.Pasted); // Hacky
        _internalTabViewModel.SourceIdentifier = null; // No file path for pasted

        _internalTabViewModel.LoadPastedContent(text); // This populates LogDoc

        // Activate to set up stream and apply filters
         _ = _internalTabViewModel.ActivateAsync(
            this.AvailableProfiles, 
            this.ContextLines,
            this.HighlightTimestamps,
            this.ShowLineNumbers,
            this.IsAutoScrollEnabled
        ).ContinueWith(t => {
            if (t.IsFaulted) Debug.WriteLine($"Error activating internal tab after paste: {t.Exception}");
        });
    }
    
    // ResetLogDocumentAndUIStateImmediate is now mostly a TabViewModel concern.
    // MainViewModel might have a "ClearCurrentTabData" command.
    private void ResetCurrentlyActiveTabData() // Renamed for clarity
    {
        _internalTabViewModel.Deactivate(); // Deactivate to stop sources/streams
        // Tell the tab to clear its specific data
        _internalTabViewModel.LogDoc.Clear(); 
        _internalTabViewModel.FilteredLogLines.Clear(); // Clears UI collection bound through delegation
        // Reset search etc. within tab through a dedicated method or as part of Load/Activate
        // _internalTabViewModel.ResetSearchState(); // Example
        // _internalTabViewModel.HighlightedFilteredLineIndex = -1; // Example

        // Reactivate if it was active, or prepare for new content.
        // For Phase 0.1, reactivating it will make it process its (now empty) LogDoc.
         _ = _internalTabViewModel.ActivateAsync(
            this.AvailableProfiles, 
            this.ContextLines,
            this.HighlightTimestamps,
            this.ShowLineNumbers,
            this.IsAutoScrollEnabled
        ).ContinueWith(t => {
             if (t.IsFaulted) Debug.WriteLine($"Error reactivating internal tab after reset: {t.Exception}");
         });;
        CurrentGlobalLogFilePathDisplay = "[Cleared]";
    }


    // OnCurrentActiveLogSourceChanged is removed from MainViewModel. TabViewModel handles its own LogSource.
    // CreateFilteredStream, SubscribeToFilteredStream, DisposeAndClearFilteredStream are removed. TabViewModel handles these.
    // ProcessTotalLinesUpdate is removed. TotalLogLines is delegated.
}
