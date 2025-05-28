using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels;

/*
 * Partial class for MainViewModel responsible for managing the log simulator.
 * This includes starting, stopping, restarting the simulator, generating bursts of log lines,
 * and handling simulator configuration. Operations are delegated to the internal TabViewModel
 * when it's configured as a simulator source.
 */
public partial class MainViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private bool _isSimulatorConfigurationVisible = false;
    [RelayCommand] private void HideSimulatorConfig() => IsSimulatorConfigurationVisible = false;

    public bool IsSimulatorRunning
    {
        get
        {
            if (_internalTabViewModel.SourceType == SourceType.Simulator &&
                _internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim) // Check TabViewModel's LogSource
            {
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
        // Apply to active simulator if it exists and is the correct type
        if (_internalTabViewModel.SourceType == SourceType.Simulator &&
            _internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
        {
            sim.LinesPerSecond = (int)Math.Round(value);
        }
        MarkSettingsAsDirty(); // Settings changed
        NotifySimulatorCommandsCanExecuteChanged();
    }

    partial void OnSimulatorErrorFrequencyChanged(double value)
    {
        if (_internalTabViewModel.SourceType == SourceType.Simulator &&
            _internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
        {
            sim.ErrorFrequency = (int)Math.Round(value);
        }
        MarkSettingsAsDirty(); // Settings changed
        NotifySimulatorCommandsCanExecuteChanged();
    }

    partial void OnSimulatorBurstSizeChanged(double value)
    {
        MarkSettingsAsDirty(); // Settings changed
        NotifySimulatorCommandsCanExecuteChanged();
    }

    private void NotifySimulatorCommandsCanExecuteChanged()
    {
        OnPropertyChanged(nameof(IsSimulatorRunning)); // This is key for ToggleButton
        (GenerateBurstCommand as IAsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ToggleSimulatorCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (RestartSimulatorCommand as IRelayCommand)?.NotifyCanExecuteChanged();
    }


    [RelayCommand(CanExecute = nameof(CanGenerateBurst))]
    private async Task GenerateBurst()
    {
        if (_internalTabViewModel.SourceType != SourceType.Simulator ||
            _internalTabViewModel.LogSourceExposeDeprecated is not ISimulatorLogSource sim)
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
    private bool CanGenerateBurst() => _internalTabViewModel.SourceType == SourceType.Simulator &&
                                     _internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim &&
                                     sim.IsRunning && // Check if the simulator is actually running
                                     SimulatorBurstSize > 0;

    /*
     * Configures and activates the internal TabViewModel to run as a log simulator.
     * If a simulator is already running, this method has no effect. Otherwise, it sets up
     * the tab's source type to Simulator and initiates its activation sequence, which
     * internally starts the log generation.
     */
    private async Task ActivateSimulatorInInternalTab()
    {
        Debug.WriteLine($"---> ActivateSimulatorInInternalTab: Entry.");
        if (IsSimulatorRunning) return; // Already running, no action needed

        try
        {
            _internalTabViewModel.DeactivateLogProcessing();
            _internalTabViewModel.SourceType = SourceType.Simulator;
            _internalTabViewModel.SourceIdentifier = "Simulator";
            _internalTabViewModel.Header = "Simulator";
            CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header;

            // TabViewModel.ActivateAsync will create the ISimulatorLogSource via LogDataProcessor
            await _internalTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled,
                null
            );

            // After activation, the LogSource should be an ISimulatorLogSource.
            // Apply current global simulator settings to this new instance.
            if (_internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource simSource)
            {
                simSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
                simSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
                // StartMonitoring is called by TabViewModel/LogDataProcessor's ActivateAsync
            }
            else
            {
                // This case should ideally not happen if ActivateAsync is correct for Simulator type
                HandleSimulatorError("Simulator source was not correctly initialized.", new InvalidOperationException("LogSource is not ISimulatorLogSource after activation."));
            }
            _uiContext.Post(_ => NotifySimulatorCommandsCanExecuteChanged(), null);
            Debug.WriteLine($"---> ActivateSimulatorInInternalTab: Exit.");
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error starting simulator", ex);
            // NotifySimulatorCommandsCanExecuteChanged is called within HandleSimulatorError
        }
    }


    /*
     * Stops the log simulator if it is currently running in the internal TabViewModel.
     * This involves telling the ISimulatorLogSource to stop generating lines.
     */
    private void StopSimulatorInInternalTab()
    {
        Debug.WriteLine($"---> StopSimulatorInInternalTab: Entry.");
        if (!IsSimulatorRunning) return; // Not running or not a simulator tab, no action.

        try
        {
            // The IsSimulatorRunning check ensures LogSource is ISimulatorLogSource
            if (_internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
            {
                sim.Stop();
            }
            NotifySimulatorCommandsCanExecuteChanged(); // Update UI state (e.g., toggle button)
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

    private bool CanRestartSimulator() => _internalTabViewModel.SourceType == SourceType.Simulator &&
                                         _internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim &&
                                         sim.IsRunning;
    [RelayCommand(CanExecute = nameof(CanRestartSimulator))]
    private void RestartSimulator()
    {
        if (!CanRestartSimulator()) return;
        try
        {
            _internalTabViewModel.DeactivateLogProcessing();
            // Ensure SourceType remains Simulator for reactivation
            _internalTabViewModel.SourceType = SourceType.Simulator;
            _internalTabViewModel.Header = "Simulator";
            CurrentGlobalLogFilePathDisplay = _internalTabViewModel.Header;

            _ = _internalTabViewModel.ActivateAsync(
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
                   HandleSimulatorError("Error restarting simulator tab", t.Exception.Flatten());
                   return;
               }
               // Apply settings to the newly created simulator instance
               if (_internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource newSimSource)
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
            if (IsSimulatorRunning) // If simulator is running, stop it before clearing
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

        // Attempt to stop simulator if it was considered running, to prevent further issues.
        // This check helps avoid trying to stop if the error occurred before it fully started.
        if (_internalTabViewModel.SourceType == SourceType.Simulator &&
            _internalTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim &&
            sim.IsRunning)
        {
            StopSimulatorInInternalTab();
        }
        NotifySimulatorCommandsCanExecuteChanged(); // Refresh UI state
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
