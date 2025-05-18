using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Logonaut.Common;
using System.Collections.Generic;
using System;
using Logonaut.UI.ViewModels;
using System.Linq;
using Logonaut.Filters;
using CommunityToolkit.Mvvm.Input; // For AsyncRelayCommand

namespace Logonaut.UI.Tests.ViewModels;

/*
 * Unit tests for MainViewModel focusing on user interactions.
 * This includes scenarios like opening log files, auto-scrolling behavior
 * in response to new log lines, and application cleanup logic.
 * Tests ensure that interactions correctly update ViewModel state, trigger
 * appropriate side effects (like log source activation), and manage resources.
 */
[TestClass] public class MainViewModel_InteractionTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        base.TestInitialize();
        base.SetupMainAndTabViewModel(); // Sets up _viewModel and _tabViewModel

        // Ensure the active profile is "Default" for consistency if tests rely on it implicitly.
        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(defaultProfileVM, "Default profile VM not found after base initialization.");

        if (defaultProfileVM.Model.RootFilter is not AndFilter) // If tests need a composite root
        {
            defaultProfileVM.SetModelRootFilter(new AndFilter());
        }
        if (_viewModel.ActiveFilterProfile != defaultProfileVM)
        {
            _viewModel.ActiveFilterProfile = defaultProfileVM;
        }
        _mockSettings?.Reset();
    }

    // Verifies: [ReqFileMonitorLiveUpdatev1] (Opening), [ReqLoadingOverlayIndicatorv1] (Trigger)
    [TestMethod] public async Task OpenLogFileCommand_CallsSourcePrepareStart_AndUpdatesViewModel()
    {
        // Arrange
        string filePath = "C:\\good\\log.txt";
        var initialLines = new List<string> { "Line 1 from file" };
        _mockFileDialog.FileToReturn = filePath;
        _mockFileLogSource.LinesForInitialRead = initialLines;
        int initialStartCount = _mockFileLogSource.StartCallCount;
        _tabViewModel.CurrentBusyStates.Clear(); // Ensure clean state before action

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow LogDataProcessor to complete activation

        // Assert LogSource Interaction (via TabViewModel's LogDataProcessor)
        Assert.AreEqual(filePath, _mockFileLogSource.PreparedSourceIdentifier, "FileLogSource PrepareAndGetInitialLinesAsync was not called with the correct path.");
        Assert.AreEqual(initialStartCount + 1, _mockFileLogSource.StartCallCount, "FileLogSource StartMonitoring (Start) was not called the expected number of times.");
        Assert.IsTrue(_mockFileLogSource.IsRunning, "FileLogSource should be running after OpenLogFile.");
        Assert.IsFalse(_mockSimulatorSource.IsRunning, "SimulatorSource should not be running.");

        // Assert ViewModel State
        Assert.AreEqual(filePath, _viewModel.CurrentGlobalLogFilePathDisplay, "Global display path incorrect.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count (delegated) mismatch.");
        Assert.AreEqual("Line 1 from file", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy states should be clear after successful load.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states should be clear.");
    }

    // Verifies: [ReqAutoScrollOptionv2], [ReqAutoScrollDisableOnManualv1] (Indirectly by checking event firing)
    [TestMethod] public async Task RequestScrollToEnd_ShouldFire_When_NewLineArrives_And_AutoScrollEnabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1" });
        _viewModel.IsAutoScrollEnabled = true; // MainViewModel controls this for TabViewModel
        _requestScrollToEndEventFired = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow LogDataProcessor and TabViewModel to process

        // Assert
        Assert.IsTrue(_requestScrollToEndEventFired, "RequestScrollToEnd event should fire for append when enabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count); // Delegated
    }

    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_NewLineArrives_And_AutoScrollDisabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1" });
        _viewModel.IsAutoScrollEnabled = false; // MainViewModel controls this
        _requestScrollToEndEventFired = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "RequestScrollToEnd event should NOT fire when auto-scroll is disabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count); // Delegated
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollEnabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1", "Line 2 FilterMe" });
        _viewModel.IsAutoScrollEnabled = true; // MainViewModel controls this
        _requestScrollToEndEventFired = false;
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "Active filter profile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(new SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate(); // This calls TabViewModel.ApplyFiltersFromProfile

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "RequestScrollToEnd event should NOT fire for replace updates (filter changes).");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch.");
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollDisabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1", "Line 2 FilterMe" });
        _viewModel.IsAutoScrollEnabled = false; // MainViewModel controls this
        _requestScrollToEndEventFired = false;
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        _viewModel.ActiveFilterProfile?.SetModelRootFilter(new SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate(); // This calls TabViewModel.ApplyFiltersFromProfile

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "RequestScrollToEnd event should NOT fire for replace updates when disabled.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch.");
    }

    // Verifies cleanup logic, indirectly related to [ReqSettingsLoadSavev1]
    [TestMethod] public async Task Cleanup_ClearsBusyStates_SavesSettings_StopsAndDisposesSources()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "File Line" });

        // Start simulator via MainViewModel, which delegates to TabViewModel
        // MainViewModel.ToggleSimulatorCommand is an IRelayCommand, but internally calls async methods.
        // For testing, we need to ensure the async operation completes.
        // If it's an AsyncRelayCommand, await it. If RelayCommand, need to manage timing.
        // Assuming MainViewModel's ToggleSimulatorCommand is now correctly an AsyncRelayCommand if needed.
        // Let's assume it's a standard RelayCommand for now and rely on scheduler advancement.
        // UPDATE: From MainViewModel.Simulator.cs, ToggleSimulator is AsyncRelayCommand, so cast and await.

        Assert.IsInstanceOfType(_viewModel.ToggleSimulatorCommand, typeof(AsyncRelayCommand), "ToggleSimulatorCommand is not AsyncRelayCommand.");
        await ((AsyncRelayCommand)_viewModel.ToggleSimulatorCommand).ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow simulator activation
        Assert.IsTrue(_mockSimulatorSource.IsRunning, "Arrange failure: Simulator source not monitoring via tab.");

        _mockSettings.Reset();
        _viewModel.CurrentGlobalBusyStates.Clear(); // Global states on MainViewModel
        _tabViewModel.CurrentBusyStates.Clear();    // Tab-specific states
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken);
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);

        // Act
        _viewModel.Cleanup(); // Calls Dispose, which should call TabViewModel's Dispose/Deactivate
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow async cleanup

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states not cleared.");
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy states not cleared.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved on cleanup.");

        // LogSource instances are managed by LogDataProcessor, which is owned by TabViewModel.
        // Cleanup/Dispose of MainViewModel should lead to TabViewModel's LogDataProcessor stopping its sources.
        Assert.IsFalse(_mockFileLogSource.IsRunning, "_mockFileLogSource should not be running after cleanup if it was the last active source before simulator.");
        // If _mockFileLogSource was never re-activated after simulator, its dispose state might depend on how TabViewModel handles source switching.
        // The key is that the *active* source (simulator) should be stopped and disposed.
        // And any *previous* source (file) should also have been disposed when the simulator took over.

        Assert.IsFalse(_mockSimulatorSource.IsRunning, "_mockSimulatorSource (used by tab) not stopped.");
        Assert.IsTrue(_mockSimulatorSource.IsDisposed, "_mockSimulatorSource (used by tab) not disposed.");

        // Check dispose state of the file source. It should have been disposed when simulator was activated.
        Assert.IsTrue(_mockFileLogSource.IsDisposed, "_mockFileLogSource was not disposed when simulator was activated or during final cleanup.");
    }

    /*
     * Helper to setup the internal TabViewModel with initial lines loaded
     * by simulating the OpenLogFileCommand.
     * This ensures the TabViewModel's LogDataProcessor is correctly initialized.
     */
    private async Task ArrangeInitialLinesForTab(IEnumerable<string> lines)
    {
        _mockFileLogSource.LinesForInitialRead.Clear();
        _mockFileLogSource.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_setup_tab.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow LogDataProcessor to activate
    }
}
