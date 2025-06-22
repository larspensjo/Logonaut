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
 * and handling simulator configuration. Operations are now delegated to the ActiveTabViewModel
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
            if (ActiveTabViewModel?.SourceType == SourceType.Simulator &&
                ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
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
        if (ActiveTabViewModel?.SourceType == SourceType.Simulator &&
            ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
        {
            sim.LinesPerSecond = (int)Math.Round(value);
        }
        MarkSettingsAsDirty(); // Settings changed
        NotifySimulatorCommandsCanExecuteChanged();
    }

    partial void OnSimulatorErrorFrequencyChanged(double value)
    {
        if (ActiveTabViewModel?.SourceType == SourceType.Simulator &&
            ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
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
        if (ActiveTabViewModel?.SourceType != SourceType.Simulator ||
            ActiveTabViewModel.LogSourceExposeDeprecated is not ISimulatorLogSource sim)
        {
            MessageBox.Show("Simulator is not the active log source for the current view.", "Simulator Not Active", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int burstCount = (int)Math.Round(SimulatorBurstSize);
        if (burstCount <= 0) return;

        _uiContext.Post(_ => ActiveTabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken), null);
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> GenerateBurst: Starting burst of {burstCount} lines.");
        try
        {
            await sim.GenerateBurstAsync(burstCount);
        }
        catch (Exception ex) { HandleSimulatorError("Error generating burst", ex); }
        finally { _uiContext.Post(_ => ActiveTabViewModel.CurrentBusyStates.Remove(TabViewModel.LoadingToken), null); }
    }
    private bool CanGenerateBurst() => ActiveTabViewModel?.SourceType == SourceType.Simulator &&
                                     ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim &&
                                     sim.IsRunning &&
                                     SimulatorBurstSize > 0;

    /*
     * Configures and activates the ActiveTabViewModel to run as a log simulator.
     */
    private async Task ActivateSimulatorInActiveTab()
    {
        Debug.WriteLine($"---> ActivateSimulatorInActiveTab: Entry.");
        if (IsSimulatorRunning || ActiveTabViewModel == null) return;

        try
        {
            ActiveTabViewModel.DeactivateLogProcessing();
            ActiveTabViewModel.SourceType = SourceType.Simulator;
            ActiveTabViewModel.SourceIdentifier = "Simulator";
            ActiveTabViewModel.Header = "Simulator";
            CurrentGlobalLogFilePathDisplay = ActiveTabViewModel.Header;

            await ActiveTabViewModel.ActivateAsync(
                this.AvailableProfiles,
                this.ContextLines,
                this.HighlightTimestamps,
                this.ShowLineNumbers,
                this.IsAutoScrollEnabled,
                null
            );

            if (ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource simSource)
            {
                simSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
                simSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
            }
            else
            {
                HandleSimulatorError("Simulator source was not correctly initialized.", new InvalidOperationException("LogSource is not ISimulatorLogSource after activation."));
            }
            _uiContext.Post(_ => NotifySimulatorCommandsCanExecuteChanged(), null);
            Debug.WriteLine($"---> ActivateSimulatorInActiveTab: Exit.");
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error starting simulator", ex);
        }
    }


    /*
     * Stops the log simulator if it is currently running in the ActiveTabViewModel.
     */
    private void StopSimulatorInActiveTab()
    {
        Debug.WriteLine($"---> StopSimulatorInActiveTab: Entry.");
        if (!IsSimulatorRunning || ActiveTabViewModel == null) return;

        try
        {
            if (ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim)
            {
                sim.Stop();
            }
            NotifySimulatorCommandsCanExecuteChanged();
            Debug.WriteLine("---> Simulator Stopped in active tab");
        }
        catch (Exception ex) { HandleSimulatorError("Error stopping simulator", ex); }
        Debug.WriteLine($"---> StopSimulatorInActiveTab: Exit.");
    }

    [RelayCommand] private async Task ToggleSimulator()
    {
        Debug.WriteLine($"---> ToggleSimulatorCommand: Entry. IsSimulatorRunning (before action): {IsSimulatorRunning}");
        if (IsSimulatorRunning)
        {
            Debug.WriteLine($"---> ToggleSimulatorCommand: Calling StopSimulatorInActiveTab.");
            StopSimulatorInActiveTab();
        }
        else
        {
            Debug.WriteLine($"---> ToggleSimulatorCommand: Calling ActivateSimulatorInActiveTab.");
            await ActivateSimulatorInActiveTab();
        }
        Debug.WriteLine($"---> ToggleSimulatorCommand: Exit. IsSimulatorRunning (after action): {IsSimulatorRunning}");
    }

    private bool CanRestartSimulator() => ActiveTabViewModel?.SourceType == SourceType.Simulator &&
                                         ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim &&
                                         sim.IsRunning;
    [RelayCommand(CanExecute = nameof(CanRestartSimulator))]
    private void RestartSimulator()
    {
        if (!CanRestartSimulator() || ActiveTabViewModel == null) return;
        try
        {
            ActiveTabViewModel.DeactivateLogProcessing();
            ActiveTabViewModel.SourceType = SourceType.Simulator;
            ActiveTabViewModel.Header = "Simulator";
            CurrentGlobalLogFilePathDisplay = ActiveTabViewModel.Header;

            _ = ActiveTabViewModel.ActivateAsync(
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
               if (ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource newSimSource)
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
            if (IsSimulatorRunning)
            {
                StopSimulatorInActiveTab();
            }
            ResetCurrentlyActiveTabData();
            Debug.WriteLine("---> Log Cleared for active tab");
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
        
        if (ActiveTabViewModel?.SourceType == SourceType.Simulator &&
            ActiveTabViewModel.LogSourceExposeDeprecated is ISimulatorLogSource sim &&
            sim.IsRunning)
        {
            StopSimulatorInActiveTab();
        }
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
