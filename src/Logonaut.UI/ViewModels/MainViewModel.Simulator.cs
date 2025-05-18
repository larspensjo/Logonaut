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

    public bool IsSimulatorRunning
    {
        get
        {
            if (_internalTabViewModel.SourceType == SourceType.Simulator &&
                _internalTabViewModel.LogSource is ISimulatorLogSource sim)
            {
                // Removed verbose logging from original for brevity
                return sim.IsRunning;
            }
            return false;
        }
    }

    [ObservableProperty] private double _simulatorLPS = 10;
    [ObservableProperty] private double _simulatorErrorFrequency = 100.0;
    [ObservableProperty] private double _simulatorBurstSize = 1000;

    partial void OnSimulatorLPSChanged(double value)
    {
        if (_internalTabViewModel.LogSource is ISimulatorLogSource sim)
            sim.LinesPerSecond = (int)Math.Round(value);
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

        _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null);
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> GenerateBurst: Starting burst of {burstCount} lines.");
        try
        {
            await sim.GenerateBurstAsync(burstCount);
        }
        catch (Exception ex) { HandleSimulatorError("Error generating burst", ex); }
        finally { _uiContext.Post(_ => _internalTabViewModel.CurrentBusyStates.Remove(TabViewModel.LoadingToken), null); }
    }
    private bool CanGenerateBurst() => IsSimulatorRunning;

    private async Task ActivateSimulatorInInternalTab()
    {
        Debug.WriteLine($"---> ActivateSimulatorInInternalTab: Entry.");
        if (IsSimulatorRunning) return;

        try
        {
            _internalTabViewModel.Deactivate();
            _internalTabViewModel.SourceType = SourceType.Simulator;
            _internalTabViewModel.SourceIdentifier = "Simulator";
            _internalTabViewModel.Header = "Simulator"; // Set tab header
            CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header; // Update global display


            await _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled
            );

            if (_internalTabViewModel.LogSource is ISimulatorLogSource simSource)
            {
                simSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
                simSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
            }
            _uiContext.Post(_ => NotifySimulatorCommandsCanExecuteChanged(), null);
            Debug.WriteLine($"---> ActivateSimulatorInInternalTab: Exit.");
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error starting simulator", ex);
            NotifySimulatorCommandsCanExecuteChanged();
        }
    }


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
            _internalTabViewModel.Deactivate();
            _internalTabViewModel.SourceType = SourceType.Simulator; // Ensure source type remains simulator
            _internalTabViewModel.Header = "Simulator"; // Reset header
            CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header; // Update global display

            _ = _internalTabViewModel.ActivateAsync(
               this.AvailableProfiles,
               this.ContextLines,
               this.HighlightTimestamps,
               this.ShowLineNumbers,
               this.IsAutoScrollEnabled
           ).ContinueWith(t =>
           {
               if (t.IsFaulted && t.Exception != null)
               {
                   HandleSimulatorError("Error restarting simulator tab", t.Exception.Flatten());
                   return;
               }
               if (_internalTabViewModel.LogSource is ISimulatorLogSource newSimSource)
               {
                   newSimSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
                   newSimSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
               }
               _uiContext.Post(_ => NotifySimulatorCommandsCanExecuteChanged(), null);
           });
        }
        catch (Exception ex) { HandleSimulatorError("Error restarting simulator", ex); }
    }

    [RelayCommand(CanExecute = nameof(CanPerformActionWhileNotLoading))]
    private void ClearLog()
    {
        try
        {
            // If simulator is running, stop it before clearing.
            if (IsSimulatorRunning)
            {
                StopSimulatorInInternalTab();
            }
            ResetCurrentlyActiveTabData();
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
        _uiContext.Post(_ =>
        {
            MessageBox.Show($"{context}: {ex.Message}", "Simulator Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }, null);
        if (IsSimulatorRunning) StopSimulatorInInternalTab();
        NotifySimulatorCommandsCanExecuteChanged();
    }

    private void LoadSimulatorPersistedSettings(LogonautSettings settings)
    {
        SimulatorLPS = settings.SimulatorLPS;
        SimulatorErrorFrequency = settings.SimulatorErrorFrequency;
        SimulatorBurstSize = settings.SimulatorBurstSize;
    }

    private void SaveSimulatorSettings(LogonautSettings settings)
    {
        settings.SimulatorLPS = SimulatorLPS;
        settings.SimulatorErrorFrequency = SimulatorErrorFrequency;
        settings.SimulatorBurstSize = SimulatorBurstSize;
    }
}
