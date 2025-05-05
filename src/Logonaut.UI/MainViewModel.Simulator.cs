using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows; // For Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels;

/*
 * Implements the portion of MainViewModel responsible for managing the log simulation feature.
 *
 * Purpose:
 * To provide user control over a simulated log data stream, allowing configuration
 * of generation parameters and interaction with the main log processing pipeline. It serves
 * as a way to test filtering and display without needing a real log file.
 *
 * Role:
 * This part of the ViewModel acts as the intermediary between the simulator UI controls
 * (sliders for rate/error/burst, start/stop/restart buttons) and the underlying
 * ISimulatorLogSource service. It manages the lifecycle of the simulator source,
 * handles switching the application's active log source between file and simulator,
 * and triggers necessary state resets (like clearing the log view when switching).
 *
 * Responsibilities:
 * - Manages simulator-specific UI state (e.g., visibility of config panel, running state).
 * - Handles commands originating from simulator UI controls.
 * - Instantiates and controls the ISimulatorLogSource via ILogSourceProvider.
 * - Updates ISimulatorLogSource parameters (rate, error frequency) based on UI interactions.
 * - Coordinates the switch of the main application's ILogSource and IReactiveFilteredLogStream.
 * - Manages simulator-specific settings persistence.
 *
 * Benefits:
 * - Isolates simulator control logic within the ViewModel layer.
 * - Facilitates testing and demonstration of log processing features.
 * - Partial class organization improves code readability for the MainViewModel.
 *
 * Implementation Notes:
 * Carefully manages the transition between file and simulator sources, including
 * conditional log clearing and processor resetting. Uses SynchronizationContext for
 * UI updates from background tasks (like burst completion).
 */
public partial class MainViewModel : ObservableObject, IDisposable
{
    private ISimulatorLogSource? _simulatorLogSource; // Keep a dedicated instance

    [ObservableProperty] private bool _isSimulatorConfigurationVisible = false;
    [RelayCommand] private void HideSimulatorConfig()
    {
        IsSimulatorConfigurationVisible = false;
    }

    [NotifyCanExecuteChangedFor(nameof(RestartSimulatorCommand))] // Add this notification
    [NotifyCanExecuteChangedFor(nameof(ToggleSimulatorCommand))] // Can always toggle? Or add specific CanExecute? Let's assume always for now.
    [ObservableProperty] private bool _isSimulatorRunning = false;

    [ObservableProperty] private double _simulatorLPS = 10; // Use double for Slider binding

    partial void OnSimulatorLPSChanged(double value)
    {
        // Update the running simulator's rate immediately via interface
        _simulatorLogSource?.UpdateRate((int)Math.Round(value));
        SaveCurrentSettingsDelayed();
    }

    [ObservableProperty] private double _simulatorErrorFrequency = 100.0;
    partial void OnSimulatorErrorFrequencyChanged(double value)
    {
        if (_simulatorLogSource != null)
        {
            _simulatorLogSource.ErrorFrequency = (int)Math.Round(value); // Update the source
            SaveCurrentSettingsDelayed();
        }
    }

    partial void OnSimulatorBurstSizeChanged(double value)
    {
        // Burst size doesn't directly affect the running simulator, only the next burst command
        SaveCurrentSettingsDelayed(); // <<< Ensure this call exists
    }

    [ObservableProperty] private double _simulatorBurstSize = 1000; // Default burst size (will be bound to slider)

    [RelayCommand(CanExecute = nameof(CanGenerateBurst))]
    private async Task GenerateBurst()
    {
        if (_simulatorLogSource == null)
        {
            // Optionally: Show a message telling the user to start the simulator first
            // Or implicitly start it? Let's require it to be "active" (started at least once).
            Debug.WriteLine("WARN: GenerateBurst called but _simulatorLogSource is null.");
            MessageBox.Show("Please start the simulator at least once before generating a burst.", "Simulator Not Active", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int burstCount = (int)Math.Round(SimulatorBurstSize); // Get size from property
        if (burstCount <= 0) return;

        // Add busy indicator token
        _uiContext.Post(_ => CurrentBusyStates.Add(BurstToken), null);
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> GenerateBurst: Starting burst of {burstCount} lines. Adding BurstToken to busy states.");

        try
        {
            // Call the simulator's burst method
            await _simulatorLogSource.GenerateBurstAsync(burstCount);
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> GenerateBurst: Burst generation task completed for {burstCount} lines.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}!!! GenerateBurst: Error during burst: {ex.Message}");
            // Show error to user
            MessageBox.Show($"Error generating burst: {ex.Message}", "Burst Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Ensure busy indicator token is removed
            _uiContext.Post(_ => {
                CurrentBusyStates.Remove(BurstToken);
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> GenerateBurst: Removing BurstToken from busy states.");
            }, null);
        }
    }

    private bool CanGenerateBurst()
    {
        // Enable Burst if the simulator source has been instantiated
        // AND is the currently selected source type.
        // This allows bursting even if the continuous rate (timer) is 0.
        return _simulatorLogSource != null && CurrentActiveLogSource == _simulatorLogSource;
    }

    private void ExecuteStartSimulatorLogic()
    {
        if (IsSimulatorRunning) return; // Guard against accidental multiple starts

        try
        {
            bool wasPreviouslyFileSource = (CurrentActiveLogSource == _fileLogSource);

            // --- Stop existing source monitoring (File or previous Simulator) ---
            if (wasPreviouslyFileSource)
            {
                _fileLogSource?.StopMonitoring();
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> StartSimulatorLogic: Stopped FileLogSource monitoring.");
            }
            _simulatorLogSource?.Stop(); // Stop previous simulator instance if any


            // --- Setup Simulator ---
            _simulatorLogSource ??= _sourceProvider.CreateSimulatorLogSource();
            if (!_disposables.Contains((IDisposable)_simulatorLogSource))
            {
                _disposables.Add((IDisposable)_simulatorLogSource);
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> StartSimulatorLogic: Added new SimulatorLogSource to disposables.");
            }

            _simulatorLogSource.LinesPerSecond = (int)Math.Round(SimulatorLPS);
            _simulatorLogSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);

            // --- Switch Active Source & Processor ---
            DisposeAndClearFilteredStream();
            CurrentActiveLogSource = _simulatorLogSource;
            _reactiveFilteredLogStream = CreateFilteredStream(CurrentActiveLogSource);
            _disposables.Add(_reactiveFilteredLogStream);
            SubscribeToFilteredStream();

            // --- Reset State CONDITIONALLY ---
            if (wasPreviouslyFileSource)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> StartSimulatorLogic: Clearing log because previous source was FileLogSource.");
                ResetLogDocumentAndUIState(); // CLEAR LOG ONLY IF SWITCHING FROM FILE
            }
            else
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff}---> StartSimulatorLogic: NOT clearing log (previous source was not FileLogSource or null). Resetting processor only.");
                _reactiveFilteredLogStream.Reset(); // ONLY RESET PROCESSOR STATE if not clearing doc
            }

            CurrentLogFilePath = "[Simulation Active]";

            // --- Prepare Simulator (NO initial lines) ---
            _simulatorLogSource.PrepareAndGetInitialLinesAsync("Simulator", AddLineToLogDocument)
                            .ContinueWith(t => { /* Error handling */ }, TaskScheduler.Default);

            // --- **** EXPLICITLY TRIGGER INITIAL FILTER **** ---
            // This call is CRUCIAL. It ensures the fullRefilterPipeline runs once,
            // processes the (empty) initial state, and resets _isInitialLoadInProgress to false.
            IFilter? firstFilter = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
            _reactiveFilteredLogStream.UpdateFilterSettings(firstFilter, ContextLines);

            _simulatorLogSource.Start(); // NOW start the timer

            IsSimulatorRunning = _simulatorLogSource.IsRunning; // Update state
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error starting simulator", ex);
            IsSimulatorRunning = false; // Ensure state is correct on error
        }
    }

    private void ExecuteStopSimulatorLogic()
    {
        if (!IsSimulatorRunning) return; // Guard

        try
        {
            _simulatorLogSource?.Stop();
            IsSimulatorRunning = _simulatorLogSource?.IsRunning ?? false; // Update state
            Debug.WriteLine("---> Simulator Stopped");
            // CurrentLogFilePath = "[Simulation Stopped]"; // Optional
        }
        catch (Exception ex)
        {
            HandleSimulatorError("Error stopping simulator", ex);
            // Optionally try to force IsSimulatorRunning to false
            IsSimulatorRunning = false;
        }
    }

    [RelayCommand] private void ToggleSimulator()
    {
        if (IsSimulatorRunning)
        {
            ExecuteStopSimulatorLogic();
        }
        else
        {
            ExecuteStartSimulatorLogic();
        }
        // Notify CanExecute changed for Restart command as its state depends on IsSimulatorRunning
        RestartSimulatorCommand.NotifyCanExecuteChanged();
    }

    private bool CanRestartSimulator() => IsSimulatorRunning && _simulatorLogSource != null;
    [RelayCommand(CanExecute = nameof(CanRestartSimulator))]
    private void RestartSimulator()
    {
        if (!IsSimulatorRunning || _simulatorLogSource == null) return; // Check source exists too

        try
        {
            // 1. Reset State (calls processor.Reset(), sets flag=true)
            ResetLogDocumentAndUIState();

            // 2. Explicitly Trigger Initial Filter (to reset the flag)
            // Explicitly trigger the initial filter pipeline.
            // This is necessary to run the logic within the fullRefilterPipeline
            // which resets the _isInitialLoadInProgress flag after processing the
            // initial (potentially empty) document state. This ensures the incremental
            // pipeline is unblocked before the source starts emitting lines.
            IFilter? currentFilter = ActiveFilterProfile?.Model?.RootFilter ?? new TrueFilter();
            _reactiveFilteredLogStream.UpdateFilterSettings(currentFilter, ContextLines);

            // 3. Restart Simulator Emissions
            //    The internal Restart likely handles PrepareAndGetInitialLinesAsync implicitly or it's not needed again.
            _simulatorLogSource.Restart(); // Call Restart via interface

            IsSimulatorRunning = _simulatorLogSource.IsRunning; // Update running state
        }
         catch (Exception ex)
        {
            HandleSimulatorError("Error restarting simulator", ex);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        try
        {
            ResetLogDocumentAndUIState();
            // If the simulator is running, it keeps running, just the view is cleared
            if (IsSimulatorRunning)
            {
                // Optionally restart simulator counter? Depends on desired behavior.
                // _simulatorLogSource?.Restart(); // Uncomment if restart desired on Clear
            }
            // Manually trigger a filter update on the now empty document
             TriggerFilterUpdate();
            Debug.WriteLine("---> Log Cleared");
        }
        catch (Exception ex)
        {
            // Handle potential errors during clearing (unlikely)
             Debug.WriteLine($"!!! Error clearing log: {ex.Message}");
             MessageBox.Show($"Error clearing log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Helper to reset document and related UI state
    private void ResetLogDocumentAndUIState()
    {
        // Reset Core State
        _reactiveFilteredLogStream.Reset(); // Resets processor's internal index and total lines observable

        // Clear Document and UI Collections/State
        _uiContext.Post(_ => {
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
            // TotalLogLines is reset by _logFilterProcessor.Reset() via its observable
        }, null);
    }

    private void HandleSimulatorError(string context, Exception ex)
    {
        Debug.WriteLine($"!!! {context}: {ex.Message}");
        MessageBox.Show($"{context}: {ex.Message}", "Simulator Error", MessageBoxButton.OK, MessageBoxImage.Error);
        // Optionally stop the simulator on error
        if (IsSimulatorRunning)
        {
            ExecuteStopSimulatorLogic();
        }
    }

    private void LoadSimulatorPersistedSettings(LogonautSettings settings)
    {
        SimulatorLPS = settings.SimulatorLPS;
        SimulatorErrorFrequency = settings.SimulatorErrorFrequency;
        SimulatorBurstSize = settings.SimulatorBurstSize;

        // Ensure simulator instance reflects loaded LPS rate if it exists
        _simulatorLogSource?.UpdateRate((int)Math.Round(SimulatorLPS));
        // Ensure simulator instance reflects loaded Error Frequency if it exists
        if (_simulatorLogSource != null)
        {
            _simulatorLogSource.ErrorFrequency = (int)Math.Round(SimulatorErrorFrequency);
        }
    }

    private void SaveSimulatorSettings(LogonautSettings settings)
    {
        settings.SimulatorLPS = SimulatorLPS;
        settings.SimulatorErrorFrequency = SimulatorErrorFrequency;
        settings.SimulatorBurstSize = SimulatorBurstSize;
    }
}
