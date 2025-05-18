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
            StopSimulatorInInternalTab(); // Stop simulator if running in the internal tab
        }

        string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*", _lastOpenedFolderPath);
        if (string.IsNullOrEmpty(selectedFile)) return;
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} MainViewModel.OpenLogFileAsync: '{selectedFile}'");

        // Use internal tab's busy state
        _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null);
        CurrentGlobalLogFilePathDisplay = selectedFile;

        try
        {
            // Deactivate first to clean up existing source/stream, if any
            _internalTabViewModel.Deactivate();

            // Reconfigure the internal tab for the new file
            _internalTabViewModel.Header = Path.GetFileName(selectedFile); // Set tab header
            _internalTabViewModel.SourceType = SourceType.File;
            _internalTabViewModel.SourceIdentifier = selectedFile;


            await _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled
            );

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
                _internalTabViewModel.LogDoc.Clear();
                _internalTabViewModel.FilteredLogLines.Clear();
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} OpenLogFileAsync: Error opening file '{selectedFile}': {ex.Message}");
                MessageBox.Show($"Error opening or reading log file '{selectedFile}':\n{ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentGlobalLogFilePathDisplay = null;
                _internalTabViewModel.LogSource?.StopMonitoring(); // Ensure source is stopped
                // Consider calling _internalTabViewModel.Deactivate() again here if it wasn't fully successful or to ensure clean state
                _internalTabViewModel.Deactivate(); // Explicitly deactivate to clear source and stream
            }, null);
        }
    }

    public void LoadLogFromText(string text)
    {
        if (_internalTabViewModel.LogSource is ISimulatorLogSource sim && sim.IsRunning)
        {
            StopSimulatorInInternalTab();
        }

        _internalTabViewModel.Deactivate();
        _internalTabViewModel.SourceType = SourceType.Pasted; // Use public setter
        _internalTabViewModel.SourceIdentifier = null;
        _internalTabViewModel.Header = "[Pasted Content]"; // Set tab header
        CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header;


        _internalTabViewModel.LoadPastedContent(text);

        _ = _internalTabViewModel.ActivateAsync(
           this.AvailableProfiles,
           this.ContextLines,
           this.HighlightTimestamps,
           this.ShowLineNumbers,
           this.IsAutoScrollEnabled
       ).ContinueWith(t =>
       {
           if (t.IsFaulted && t.Exception != null) Debug.WriteLine($"Error activating internal tab after paste: {t.Exception.Flatten().Message}");
       });
    }

    private void ResetCurrentlyActiveTabData()
    {
        _internalTabViewModel.Deactivate();
        _internalTabViewModel.LogDoc.Clear();
        _internalTabViewModel.FilteredLogLines.Clear();

        // Reset other relevant states within TabViewModel if necessary (e.g., search, selection)
        // For now, ActivateAsync re-initializing the stream and applying filters should handle most of it.

        _ = _internalTabViewModel.ActivateAsync(
           this.AvailableProfiles,
           this.ContextLines,
           this.HighlightTimestamps,
           this.ShowLineNumbers,
           this.IsAutoScrollEnabled
       ).ContinueWith(t =>
       {
           if (t.IsFaulted && t.Exception != null) Debug.WriteLine($"Error reactivating internal tab after reset: {t.Exception.Flatten().Message}");
       });
        _internalTabViewModel.Header = "[Cleared]"; // Update tab header
        CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header;
    }
}
