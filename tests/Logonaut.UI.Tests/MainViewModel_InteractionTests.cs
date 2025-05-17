using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Logonaut.Common;
using System.Collections.Generic;
using System;
using Logonaut.UI.ViewModels;
using System.Linq;
using Logonaut.Filters;
using CommunityToolkit.Mvvm.Input;

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests related to user interactions like opening files, auto-scroll, cleanup.
/// </summary>
[TestClass] public class MainViewModel_InteractionTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();

        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(defaultProfileVM, "Default profile VM not found after base initialization.");

        if (defaultProfileVM.Model.RootFilter is not AndFilter)
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
        _tabViewModel.CurrentBusyStates.Clear();

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert LogSource Interaction (via TabViewModel's LogSource)
        Assert.AreEqual(filePath, _mockFileLogSource.PreparedSourceIdentifier);
        Assert.AreEqual(initialStartCount + 1, _mockFileLogSource.StartCallCount);
        Assert.IsTrue(_mockFileLogSource.IsRunning);
        Assert.IsFalse(_mockSimulatorSource.IsRunning);

        // Assert ViewModel State
        Assert.AreEqual(filePath, _viewModel.CurrentGlobalLogFilePathDisplay, "Global display path incorrect.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count mismatch (delegated).");
        Assert.AreEqual("Line 1 from file", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy states should be clear.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states should be clear.");
    }

    // Verifies: [ReqAutoScrollOptionv2], [ReqAutoScrollDisableOnManualv1] (Indirectly by checking event firing)
    [TestMethod] public async Task RequestScrollToEnd_ShouldFire_When_NewLineArrives_And_AutoScrollEnabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1" });
        _viewModel.IsAutoScrollEnabled = true;
        _requestScrollToEndEventFired = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsTrue(_requestScrollToEndEventFired, "Event should fire for append when enabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_NewLineArrives_And_AutoScrollDisabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1" });
        _viewModel.IsAutoScrollEnabled = false;
        _requestScrollToEndEventFired = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire when auto-scroll is disabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollEnabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1", "Line 2 FilterMe" });
        _viewModel.IsAutoScrollEnabled = true;
        _requestScrollToEndEventFired = false;
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "Active filter profile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(new SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate();

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire for replace updates.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch.");
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollDisabled()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "Line 1", "Line 2 FilterMe" });
        _viewModel.IsAutoScrollEnabled = false;
        _requestScrollToEndEventFired = false;
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        _viewModel.ActiveFilterProfile?.SetModelRootFilter(new SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate();

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire for replace updates when disabled.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch.");
    }

    // Verifies cleanup logic, indirectly related to [ReqSettingsLoadSavev1]
    [TestMethod] public async Task Cleanup_ClearsBusyStates_SavesSettings_StopsAndDisposesSources()
    {
        // Arrange
        await ArrangeInitialLinesForTab(new List<string> { "File Line" });

        // ToggleSimulatorCommand is now async
        await ((AsyncRelayCommand)_viewModel.ToggleSimulatorCommand).ExecuteAsync(null); // MODIFIED to await AsyncRelayCommand
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        Assert.IsTrue(_mockSimulatorSource.IsRunning, "Arrange failure: Simulator source not monitoring via tab.");

        _mockSettings.Reset();
        _viewModel.CurrentGlobalBusyStates.Clear();
        _tabViewModel.CurrentBusyStates.Clear();
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.LoadingToken);
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);

        // Act
        _viewModel.Cleanup();
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states not cleared.");
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy states not cleared.");
        Assert.IsNotNull(_mockSettings.SavedSettings);

        Assert.IsFalse(_mockFileLogSource.IsRunning, "_mockFileLogSource should not be running.");
        Assert.IsTrue(_mockFileLogSource.IsDisposed, "_mockFileLogSource not disposed.");

        Assert.IsFalse(_mockSimulatorSource.IsRunning, "_mockSimulatorSource (used by tab) not stopped.");
        Assert.IsTrue(_mockSimulatorSource.IsDisposed, "_mockSimulatorSource (used by tab) not disposed.");
    }

    // Helper to setup VM's internal tab with initial lines loaded via OpenLogFile simulation
    private async Task ArrangeInitialLinesForTab(IEnumerable<string> lines)
    {
        _mockFileLogSource.LinesForInitialRead.Clear();
        _mockFileLogSource.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_setup_tab.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
    }
}
