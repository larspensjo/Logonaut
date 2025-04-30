using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Logonaut.Common;
using System.Collections.Generic;
using System; // For ObjectDisposedException
using Logonaut.UI.ViewModels;

namespace Logonaut.UI.Tests.ViewModels;

 /// Tests related to user interactions like opening files, auto-scroll, cleanup
[TestClass]
public class MainViewModel_InteractionTests : MainViewModelTestBase
{
    // Verifies: [ReqFileMonitorLiveUpdatev1] (Opening), [ReqLoadingOverlayIndicatorv1] (Trigger)
    [TestMethod] public async Task OpenLogFileCommand_CallsProcessorReset_AndSourcePrepareStart()
    {
        // Arrange
        string filePath = "C:\\good\\log.txt";
        _mockFileDialog.FileToReturn = filePath;
        _mockProcessor.ResetCounters(); // Reset processor mock state
        var initialStartCount = _mockLogSource.StartMonitoringCallCount;

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _testContext.Send(_ => { }, null); // Ensure posts run

        // Assert Processor Interaction
        Assert.AreEqual(1, _mockProcessor.ResetCallCount, "Processor Reset should be called once.");

        // Assert LogSource Interaction
        Assert.AreEqual(filePath, _mockLogSource.PreparedSourceIdentifier, "LogSource Prepare should be called.");
        Assert.AreEqual(initialStartCount + 1, _mockLogSource.StartMonitoringCallCount, "LogSource StartMonitoring should be called once.");
        Assert.IsTrue(_mockLogSource.IsMonitoring, "LogSource should be monitoring.");

        // Assert ViewModel State
        Assert.AreEqual(filePath, _viewModel.CurrentLogFilePath, "ViewModel CurrentLogFilePath should be updated.");
    }

    // Verifies: [ReqAutoScrollOptionv1], [ReqAutoScrollDisableOnManualv1]
    [TestMethod] public void RequestScrollToEnd_ShouldFire_When_UpdateAppendsLines_And_AutoScrollEnabled()
    {
        // Arrange
        _viewModel.IsAutoScrollEnabled = true; // Auto-scroll IS enabled
        _requestScrollToEndEventFired = false; // Reset flag
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line 1")); // Initial state

        var newFullList = new List<FilteredLogLine> {
            new FilteredLogLine(1, "Line 1"),
            new FilteredLogLine(2, "Line 2 Appended")
        };

        // Act
        _mockProcessor.SimulateReplaceUpdate(newFullList);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert
        Assert.IsTrue(_requestScrollToEndEventFired, "Event should fire for append when enabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv1]
    [TestMethod] public void RequestScrollToEnd_ShouldNotFire_When_UpdateAppendsLines_And_AutoScrollDisabled()
    {
        // Arrange
        _viewModel.IsAutoScrollEnabled = false; // Auto-scroll IS disabled
        _requestScrollToEndEventFired = false; // Reset flag
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line 1"));

        var newFullList = new List<FilteredLogLine> {
            new FilteredLogLine(1, "Line 1"),
            new FilteredLogLine(2, "Line 2 Appended")
        };

        // Act
        _mockProcessor.SimulateReplaceUpdate(newFullList);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire when auto-scroll is disabled.");
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv1]
    [TestMethod] public void RequestScrollToEnd_ShouldNotFire_When_UpdateIsReplace_And_AutoScrollEnabled()
    {
        // Arrange
        _viewModel.IsAutoScrollEnabled = true; // Auto-scroll IS enabled
        _requestScrollToEndEventFired = false; // Reset flag
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line 1"));

        var replacingList = new List<FilteredLogLine> { new FilteredLogLine(5, "Filtered Line A") };

        // Act
        _mockProcessor.SimulateReplaceUpdate(replacingList);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire for replace updates.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv1]
    [TestMethod] public void RequestScrollToEnd_ShouldNotFire_When_UpdateIsReplace_And_AutoScrollDisabled()
    {
        // Arrange
        _viewModel.IsAutoScrollEnabled = false; // Auto-scroll IS disabled
        _requestScrollToEndEventFired = false; // Reset flag
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line 1"));

        var replacingList = new List<FilteredLogLine> { new FilteredLogLine(5, "Filtered Line A") };

        // Act
        _mockProcessor.SimulateReplaceUpdate(replacingList);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert
        Assert.IsFalse(_requestScrollToEndEventFired, "Event should NOT fire for replace updates when disabled.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count);
    }

    // Verifies cleanup logic, indirectly related to [ReqSettingsLoadSavev1]
    [TestMethod] public async Task Cleanup_ClearsBusyStates_SavesSettings_StopsSource_DisposesProcessorAndSource()
    {
        // Arrange
        _mockSettings.ResetSettings();
        var processor = _mockProcessor; // Capture instance
        var source = _mockLogSource;    // Capture instance
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.LoadingToken);
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);
        await source.PrepareAndGetInitialLinesAsync("C:\\cleanup_test.log", _ => { });
        source.StartMonitoring();
        Assert.IsTrue(source.IsMonitoring, "Arrange failure: Source not monitoring.");

        // Act
        _viewModel.Cleanup(); // Calls Dispose internally
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy states not cleared.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings not saved.");
        Assert.IsFalse(source.IsMonitoring, "Source monitoring not stopped.");

        // Assert processor disposed by VM
        var odeReset = Assert.ThrowsException<ObjectDisposedException>(() => processor.Reset());
        Assert.AreEqual(nameof(Logonaut.TestUtils.MockLogFilterProcessor), odeReset.ObjectName);

        // Assert source disposed by VM
        var odePrepare = Assert.ThrowsException<ObjectDisposedException>(() => source.PrepareAndGetInitialLinesAsync("test", _ => { }));
        Assert.AreEqual(nameof(Logonaut.TestUtils.MockLogSource), odePrepare.ObjectName);
        Assert.IsTrue(source.IsDisposed, "Source not disposed.");
    }
}
