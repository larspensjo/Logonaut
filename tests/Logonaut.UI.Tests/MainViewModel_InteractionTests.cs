using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Logonaut.Common;
using System.Collections.Generic;
using System;
using Logonaut.UI.ViewModels;
using System.Linq;

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests related to user interactions like opening files, auto-scroll, cleanup.
/// </summary>
[TestClass] public class MainViewModel_InteractionTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        base.TestInitialize(); // Call the base TestInitialize - this loads the initial "Default" profile

        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");

        if (defaultProfileVM == null)
        {
            // If base.TestInitialize() doesn't guarantee a "Default" profile, create one.
            // For consistency, ensure it starts with a clean AndFilter root.
            var defaultProfileModel = new FilterProfile("Default", new Logonaut.Filters.AndFilter());
            defaultProfileVM = new FilterProfileViewModel(defaultProfileModel, _viewModel);
            _viewModel.AvailableProfiles.Add(defaultProfileVM);
            // No need to set _viewModel.ActiveFilterProfile here if the constructor/base does it
            // or if the following SetModelRootFilter ensures it's correct.
        }
        else
        {
            // Ensure the existing default profile's model has a NEW, EMPTY AndFilter root for these tests.
            // This guarantees no pre-existing children.
            defaultProfileVM.SetModelRootFilter(new Logonaut.Filters.AndFilter());
        }

        // Ensure this profile is active.
        // This might trigger saves if the profile was different or if SetModelRootFilter caused observable changes.
        if (_viewModel.ActiveFilterProfile != defaultProfileVM)
        {
            _viewModel.ActiveFilterProfile = defaultProfileVM;
        }

        // Crucial: Flush any synchronization context posted actions (like saves from profile setup)
        // BEFORE resetting the mock settings service.
        _testContext?.Send(_ => { }, null);

        // Reset mock settings AFTER the profile state is stabilized for the tests.
        // This ensures SaveCalledCount and SavedSettings only reflect actions performed by the test method itself.
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
        _viewModel.CurrentBusyStates.Clear();

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        _testContext.Send(_ => { }, null); // Process UI queue posts

        // Assert LogSource Interaction
        Assert.AreEqual(filePath, _mockFileLogSource.PreparedSourceIdentifier);
        Assert.AreEqual(initialStartCount + 1, _mockFileLogSource.StartCallCount);
        Assert.IsTrue(_mockFileLogSource.IsRunning);
        Assert.IsFalse(_mockSimulatorSource.IsRunning);

        // Assert ViewModel State
        Assert.AreEqual(filePath, _viewModel.CurrentLogFilePath);
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count mismatch after open.");
        Assert.AreEqual("Line 1 from file", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy states should be clear after open completes.");
    }

    // Verifies: [ReqAutoScrollOptionv2], [ReqAutoScrollDisableOnManualv1] (Indirectly by checking event firing)
    [TestMethod]
    public async Task RequestScrollToEnd_ShouldFire_When_NewLineArrives_And_AutoScrollEnabled()
    {
        // Arrange
        await SetupWithInitialLines(new List<string> { "Line 1" });
        _viewModel.IsAutoScrollEnabled = true;
        _requestScrollToEndEventFired = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        _testContext.Send(_ => { }, null); // Process UI queue posts

        // Assert
        Assert.IsTrue(_requestScrollToEndEventFired, "Event should fire for append when enabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count); // 1 initial + 1 appended
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_NewLineArrives_And_AutoScrollDisabled()
    {
        // Arrange
        await SetupWithInitialLines(new List<string> { "Line 1" });
        _viewModel.IsAutoScrollEnabled = false;
        _requestScrollToEndEventFired = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        _testContext.Send(_ => { }, null); // Process UI queue posts

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire when auto-scroll is disabled.");
        // <<<< FIX: Now expect 2 lines >>>>
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollEnabled()
    {
        // Arrange
        await SetupWithInitialLines(new List<string> { "Line 1", "Line 2 FilterMe" });
        var doc = _viewModel.LogDoc;
        _viewModel.IsAutoScrollEnabled = true;
        _requestScrollToEndEventFired = false;
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "Active filter profile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(new Filters.SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate();

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire for replace updates.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch."); // Expect 1 line
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollDisabled()
    {
        // Arrange
        await SetupWithInitialLines(new List<string> { "Line 1", "Line 2 FilterMe" });
        _viewModel.IsAutoScrollEnabled = false;
        _requestScrollToEndEventFired = false;
         Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        _viewModel.ActiveFilterProfile?.SetModelRootFilter(new Filters.SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate();

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire for replace updates when disabled.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch."); // Expect 1 line
    }

    // Verifies cleanup logic, indirectly related to [ReqSettingsLoadSavev1]
    [TestMethod] public async Task Cleanup_ClearsBusyStates_SavesSettings_StopsAndDisposesSources()
    {
        // Arrange
        await SetupWithInitialLines(new List<string> { "File Line" });
        _viewModel.ToggleSimulatorCommand.Execute(null);
        _testContext.Send(_ => { }, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        Assert.IsTrue(_mockSimulatorSource.IsRunning, "Arrange failure: Simulator source not monitoring.");

        _mockSettings.Reset();
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.LoadingToken);
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);

        // Act
        _viewModel.Cleanup();
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.IsFalse(_mockFileLogSource.IsRunning);
        Assert.IsTrue(_mockFileLogSource.IsDisposed);
        Assert.IsFalse(_mockSimulatorSource.IsRunning);
        Assert.IsTrue(_mockSimulatorSource.IsDisposed);
    }

    // Helper remains the same
    // Helper to setup VM with initial lines loaded via OpenLogFile simulation
    private async Task SetupWithInitialLines(IEnumerable<string> lines)
    {
        var source = _mockFileLogSource;
        source.LinesForInitialRead.Clear();
        source.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_setup.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        _testContext.Send(_ => { }, null);
    }
}
