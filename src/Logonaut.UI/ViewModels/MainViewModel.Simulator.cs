using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private bool _isSimulatorConfigurationVisible = false;
    [RelayCommand] private void HideSimulatorConfig() => IsSimulatorConfigurationVisible = false;

    // IsSimulatorRunning now reflects the state of the _internalTabViewModel's source
    public bool IsSimulatorRunning
    {
         get
         {            
             // Check if the tab is configured as a Simulator type AND its LogSource is an ISimulatorLogSource
             if (_internalTabViewModel.SourceType == SourceType.Simulator &&
                 _internalTabViewModel.LogSource is ISimulatorLogSource sim)
             {
                 Debug.WriteLine($"---> MainViewModel.IsSimulatorRunning_get: Tab's SourceType is Simulator. LogSource is ISimulatorLogSource. IsRunning: {sim.IsRunning}");
                 return sim.IsRunning;
             }
             // Log detailed reason if not considered running
             if (_internalTabViewModel.SourceType != SourceType.Simulator) {
                 Debug.WriteLine($"---> MainViewModel.IsSimulatorRunning_get: Tab's SourceType is NOT Simulator (Type: {_internalTabViewModel.SourceType}, LogSource: {_internalTabViewModel.LogSource?.GetType().Name ?? "null"}). Returning false.");
             } else { // SourceType IS Simulator, but LogSource is not ISimulatorLogSource or is null
                  Debug.WriteLine($"---> MainViewModel.IsSimulatorRunning_get: Tab's SourceType is Simulator, but LogSource is not ISimulatorLogSource (Type: {_internalTabViewModel.LogSource?.GetType().Name ?? "null"}). Returning false.");
             }
             return false;
         }
    }

    // Global simulator settings remain in MainViewModel
    [ObservableProperty] private double _simulatorLPS = 10;
    [ObservableProperty] private double _simulatorErrorFrequency = 100.0;
    [ObservableProperty] private double _simulatorBurstSize = 1000;

    partial void OnSimulatorLPSChanged(double value)
    {
        if (_internalTabViewModel.LogSource is ISimulatorLogSource sim)
            sim.LinesPerSecond = (int)Math.Round(value); // Use LinesPerSecond setter
        SaveCurrentSettingsDelayed();
        NotifySimulatorCommandsCanExecuteChanged();
    }

    partial void OnSimulatorErrorFrequencyChanged(double value)
    {
        if (_internalTabViewModel.LogSource is ISimulatorLogSource sim)
            sim.ErrorFrequency = (int)Math.Round(value);
        SaveCurrentSettingsDelayed();
        NotifySimulatorCommandsCanExecuteChanged();
    }
    
    partial void OnSimulatorBurstSizeChanged(double value)
    {
        SaveCurrentSettingsDelayed();
        NotifySimulatorCommandsCanExecuteChanged();
    }
    
    private void NotifySimulatorCommandsCanExecuteChanged()
    {
        // 1. Notify that IsSimulatorRunning might have changed.
        // This allows UI elements bound directly to IsSimulatorRunning (like ToggleButton.IsChecked) to update.
        OnPropertyChanged(nameof(IsSimulatorRunning));

        // 2. Then, notify commands that their CanExecute status might need re-evaluation.
        // This is important if their CanExecute depends on IsSimulatorRunning or other related states.
        (GenerateBurstCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ToggleSimulatorCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (RestartSimulatorCommand as IRelayCommand)?.NotifyCanExecuteChanged();
    }


    [RelayCommand(CanExecute = nameof(CanGenerateBurst))]
    private async Task GenerateBurst()
    {
        if (_internalTabViewModel.LogSource is not ISimulatorLogSource sim)
        {
            MessageBox.Show("Simulator is not the active log source for the current view.", "Simulator Not Active", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int burstCount = (int)Math.Round(SimulatorBurstSize);
        if (burstCount <= 0) return;

        _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null); // Use tab's busy token
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> GenerateBurst: Starting burst of {burstCount} lines.");
        try
        {
            await sim.GenerateBurstAsync(burstCount);
        }
        catch (Exception ex) { HandleSimulatorError("Error generating burst", ex); }
        finally { _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.LoadingToken), null); }
    }
    private bool CanGenerateBurst() => _internalTabViewModel.LogSource is ISimulatorLogSource && SimulatorBurstSize > 0;

    private async Task ActivateSimulatorInInternalTab()
    {
        Debug.WriteLine($"---> ActivateSimulatorInInternalTab: Entry.");
        if (IsSimulatorRunning) return;

        try
        {
            _internalTabViewModel.Deactivate(); // Deactivate current source in tab
            // Reconfigure _internalTabViewModel to be a Simulator tab
            _internalTabViewModel.SourceType = SourceType.Simulator;
            _internalTabViewModel.SourceIdentifier = "Simulator"; // Ensure non-empty identifier
            _internalTabViewModel.Header = "Simulator";

            // Await the activation of the tab
            await _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled
            );

            // Post-activation: apply global settings to the newly created simulator source
            if (_internalTabViewModel.LogSource is ISimulatorLogSource simSource)
            {
                simSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
                simSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
                // StartMonitoring is called within TabViewModel's ActivateAsync for Simulator source type
            }
            _uiContext.Post(_ => NotifySimulatorCommandsCanExecuteChanged(), null);
            CurrentGlobalLogFilePathDisplay = "[Simulation Active]";
            Debug.WriteLine($"---> ActivateSimulatorInInternalTab: Exit.");
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error starting simulator", ex);
            NotifySimulatorCommandsCanExecuteChanged();
        }
    }


    // Renamed from ExecuteStopSimulatorLogic
    private void StopSimulatorInInternalTab()
    {
        Debug.WriteLine($"---> StopSimulatorInInternalTab: Entry.");
        if (!IsSimulatorRunning) return;
        try
        {
            if (_internalTabViewModel.LogSource is ISimulatorLogSource sim) sim.Stop();
            NotifySimulatorCommandsCanExecuteChanged();
            Debug.WriteLine("---> Simulator Stopped in internal tab");
        }
        catch (Exception ex) { HandleSimulatorError("Error stopping simulator", ex); }
        Debug.WriteLine($"---> StopSimulatorInInternalTab: Exit.");
    }

    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private async Task ToggleSimulator()
    {
        Debug.WriteLine($"---> ToggleSimulatorCommand: Entry. IsSimulatorRunning (before action): {IsSimulatorRunning}");
        if (IsSimulatorRunning)
        {
            Debug.WriteLine($"---> ToggleSimulatorCommand: Calling StopSimulatorInInternalTab.");
            StopSimulatorInInternalTab();
        }
        else
        {
            Debug.WriteLine($"---> ToggleSimulatorCommand: Calling ActivateSimulatorInInternalTab.");
            await ActivateSimulatorInInternalTab();
        }
        Debug.WriteLine($"---> ToggleSimulatorCommand: Exit. IsSimulatorRunning (after action): {IsSimulatorRunning}");
    }

    private bool CanRestartSimulator() => _internalTabViewModel.LogSource is ISimulatorLogSource sim && sim.IsRunning;
    [RelayCommand(CanExecute = nameof(CanRestartSimulator))]
    private void RestartSimulator()
    {
        if (!CanRestartSimulator()) return;
        try
        {
            // Deactivate and Reactivate the tab, which will reset its LogDoc and restart the simulator source.
            _internalTabViewModel.Deactivate();
            // Ensure SourceType remains Simulator
            _internalTabViewModel.SourceType = SourceType.Simulator;
             _ = _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles, 
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled
            ).ContinueWith(t => {
                 if (t.IsFaulted)
                 {
                     HandleSimulatorError("Error restarting simulator tab", t.Exception!);
                     return;
                 }
                 if (_internalTabViewModel.LogSource is ISimulatorLogSource newSimSource)
                 {
                     newSimSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
                     newSimSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
                     // newSimSource.Restart(); // ActivateAsync -> StartMonitoring should handle the restart
                 }
                 _uiContext.Post(_ => NotifySimulatorCommandsCanExecuteChanged(), null);
             });
        }
        catch (Exception ex) { HandleSimulatorError("Error restarting simulator", ex); }
    }

    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private void ClearLog() // This command clears the data of the _internalTabViewModel
    {
        try
        {
            ResetCurrentlyActiveTabData(); // This deactivates, clears, and reactivates the tab
            Debug.WriteLine("---> Log Cleared for internal tab");
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"!!! Error clearing log: {ex.Message}");
             MessageBox.Show($"Error clearing log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleSimulatorError(string context, Exception ex)
    {
        Debug.WriteLine($"!!! {context}: {ex.Message}");
        MessageBox.Show($"{context}: {ex.Message}", "Simulator Error", MessageBoxButton.OK, MessageBoxImage.Error);
        if (IsSimulatorRunning) StopSimulatorInInternalTab(); // Attempt to stop
        NotifySimulatorCommandsCanExecuteChanged();
    }

    private void LoadSimulatorPersistedSettings(LogonautSettings settings)
    {
        SimulatorLPS = settings.SimulatorLPS;
        SimulatorErrorFrequency = settings.SimulatorErrorFrequency;
        SimulatorBurstSize = settings.SimulatorBurstSize;
        // Applying these to an active simulator tab happens in OnSimulatorLPSChanged etc.
        // or when a simulator tab is activated.
    }

    private void SaveSimulatorSettings(LogonautSettings settings)
    {
        settings.SimulatorLPS = SimulatorLPS;
        settings.SimulatorErrorFrequency = SimulatorErrorFrequency;
        settings.SimulatorBurstSize = SimulatorBurstSize;
    }
}
