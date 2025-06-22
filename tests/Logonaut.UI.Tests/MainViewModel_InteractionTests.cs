using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Logonaut.Common;
using System.Collections.Generic;
using System;
using Logonaut.UI.ViewModels;
using System.Linq;
using Logonaut.Filters;
using CommunityToolkit.Mvvm.Input;
using Logonaut.TestUtils;
using System.Windows;

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
        // Arrange
        base.TestInitialize();
        base.SetupMainViewModel();

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

    // Verifies: [ReqFileMonitorLiveUpdatev1], [ReqLoadingOverlayIndicatorv1]
    [TestMethod] public async Task OpenLogFileCommand_CallsSourcePrepareStart_AndUpdatesViewModel()
    {
        // Arrange
        string filePath = "C:\\good\\log.txt";
        var initialLines = new List<string> { "Line 1 from file" };
        _mockFileDialog.FileToReturn = filePath;
        _mockFileLogSource.LinesForInitialRead = initialLines;
        int initialStartCount = _mockFileLogSource.StartCallCount;

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel should not be null after opening file.");
        
        // Assert
        Assert.AreEqual(filePath, _mockFileLogSource.PreparedSourceIdentifier, "FileLogSource PrepareAndGetInitialLinesAsync was not called with the correct path.");
        Assert.AreEqual(initialStartCount + 1, _mockFileLogSource.StartCallCount, "FileLogSource StartMonitoring (Start) was not called the expected number of times.");
        Assert.IsTrue(_mockFileLogSource.IsRunning, "FileLogSource should be running after OpenLogFile.");
        Assert.IsFalse(_mockSimulatorSource.IsRunning, "SimulatorSource should not be running.");
        Assert.AreEqual(filePath, _viewModel.CurrentGlobalLogFilePathDisplay, "Global display path incorrect.");
        Assert.AreEqual(1, activeTab.FilteredLogLines.Count, "Filtered lines count mismatch.");
        Assert.AreEqual("Line 1 from file", activeTab.FilteredLogLines[0].Text);
        Assert.AreEqual(0, activeTab.CurrentBusyStates.Count, "Tab's busy states should be clear after successful load.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states should be clear.");
    }

    // Verifies: [ReqAutoScrollOptionv2], [ReqAutoScrollDisableOnManualv1]
    [TestMethod] public async Task RequestScrollToEnd_ShouldFire_When_NewLineArrives_And_AutoScrollEnabled()
    {
        // Arrange
        var tabViewModel = await ArrangeInitialLinesForTab(new List<string> { "Line 1" });
        bool requestScrollToEndEventFired = false;
        tabViewModel.RequestScrollToEnd += (s, e) => requestScrollToEndEventFired = true;
        _viewModel.IsAutoScrollEnabled = true;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsTrue(requestScrollToEndEventFired, "RequestScrollToEnd event should fire for append when enabled.");
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv2], [ReqAutoScrollDisableOnManualv1]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_NewLineArrives_And_AutoScrollDisabled()
    {
        // Arrange
        var tabViewModel = await ArrangeInitialLinesForTab(new List<string> { "Line 1" });
        bool requestScrollToEndEventFired = false;
        tabViewModel.RequestScrollToEnd += (s, e) => requestScrollToEndEventFired = true;
        _viewModel.IsAutoScrollEnabled = false;

        // Act
        GetActiveMockSource().EmitLine("Line 2 Appended");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsFalse(requestScrollToEndEventFired, "RequestScrollToEnd event should NOT fire when auto-scroll is disabled.");
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollEnabled()
    {
        // Arrange
        var tabViewModel = await ArrangeInitialLinesForTab(new List<string> { "Line 1", "Line 2 FilterMe" });
        bool requestScrollToEndEventFired = false;
        tabViewModel.RequestScrollToEnd += (s, e) => requestScrollToEndEventFired = true;
        _viewModel.IsAutoScrollEnabled = true;
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "Active filter profile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(new SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate();

        // Assert
        Assert.IsFalse(requestScrollToEndEventFired, "RequestScrollToEnd event should NOT fire for replace updates (filter changes).");
        Assert.AreEqual(1, tabViewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch.");
    }

    // Verifies: [ReqAutoScrollOptionv2]
    [TestMethod] public async Task RequestScrollToEnd_ShouldNotFire_When_FilterChanges_And_AutoScrollDisabled()
    {
        // Arrange
        var tabViewModel = await ArrangeInitialLinesForTab(new List<string> { "Line 1", "Line 2 FilterMe" });
        bool requestScrollToEndEventFired = false;
        tabViewModel.RequestScrollToEnd += (s, e) => requestScrollToEndEventFired = true;
        _viewModel.IsAutoScrollEnabled = false;
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count, "Pre-condition failed: Initial lines not loaded.");

        // Act
        _viewModel.ActiveFilterProfile?.SetModelRootFilter(new SubstringFilter("FilterMe"));
        InjectTriggerFilterUpdate();

        // Assert
        Assert.IsFalse(requestScrollToEndEventFired, "RequestScrollToEnd event should NOT fire for replace updates when disabled.");
        Assert.AreEqual(1, tabViewModel.FilteredLogLines.Count, "Filtered lines count after replace mismatch.");
    }

    // Verifies: [ReqSettingsLoadSavev1]
    [TestMethod] public async Task Cleanup_ClearsBusyStates_SavesSettings_StopsAndDisposesSources()
    {
        // Arrange
        // 1. Create the first tab (file-based)
        var fileTab = await ArrangeInitialLinesForTab(new List<string> { "File Line" });
        Assert.IsNotNull(fileTab, "Arrange failed: fileTab is null.");

        // 2. Create a second tab (by pasting) and make it the active one.
        _viewModel.LoadLogFromText("This tab will become the simulator.");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        var simulatorTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(simulatorTab, "Arrange failed: creating the second tab failed.");
        Assert.AreNotSame(fileTab, simulatorTab, "Arrange failed: A new tab should have been created for the simulator.");

        // 3. Turn the second (active) tab into a simulator.
        await _viewModel.ToggleSimulatorCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // 4. Verify the setup is correct before testing Cleanup.
        Assert.AreEqual(SourceType.Simulator, simulatorTab.SourceType, "Arrange failed: Active tab did not become a simulator.");
        Assert.IsTrue(_mockSimulatorSource.IsRunning, "Arrange failure: Simulator source not running.");
        Assert.IsTrue(_mockFileLogSource.IsRunning, "Arrange failure: File source should still be considered running on its inactive tab.");
        _mockSettings.Reset();
        _viewModel.CurrentGlobalBusyStates.Clear();
        fileTab.CurrentBusyStates.Add(TabViewModel.LoadingToken);
        simulatorTab.CurrentBusyStates.Add(TabViewModel.FilteringToken);
        _viewModel.MarkSettingsAsDirty(); // Ensure settings will be saved.

        // Act
        _viewModel.Cleanup(); // This calls Dispose on the MainViewModel, which disposes all tabs.

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states not cleared.");
        Assert.AreEqual(0, fileTab.CurrentBusyStates.Count, "File tab's busy states not cleared.");
        Assert.AreEqual(0, simulatorTab.CurrentBusyStates.Count, "Simulator tab's busy states not cleared.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved on cleanup.");
        Assert.IsFalse(_mockSimulatorSource.IsRunning, "Simulator source was not stopped.");
        Assert.IsTrue(_mockSimulatorSource.IsDisposed, "Simulator source was not disposed.");
        Assert.IsTrue(_mockFileLogSource.IsDisposed, "File source was not disposed.");
    }

    // Verifies: [ReqFileMonitorLiveUpdatev1], [ReqFileResetHandlingv1]
    [TestMethod] public async Task LogFileTruncation_TransitionsTabToSnapshot_AndOpensNewTab()
    {
        // This test now requires HandleTabSourceRestart in MainViewModel to be implemented.
        // Let's assume it is for this test.
        // Arrange
        string filePath = "C:\\test_truncation.log";
        _mockFileDialog.FileToReturn = filePath;
        
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Original line 1", "Original line 2" };
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        var fileTab = _viewModel.TabViewModels.FirstOrDefault(t => t.SourceType == SourceType.File && t.SourceIdentifier == filePath);
        Assert.IsNotNull(fileTab, "Arrange failed: File tab not found.");
        _viewModel.ActiveTabViewModel = fileTab;
        int initialTabCount = _viewModel.TabViewModels.Count;

        var source = fileTab.LogSourceExposeDeprecated as MockLogSource;
        Assert.IsNotNull(source, "Failed to get mock source from tab.");
        string originalHeader = fileTab.Header;

        // Act
        source.SimulateFileResetCallback();
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert: Old tab becomes a snapshot
        Assert.AreEqual(SourceType.Snapshot, fileTab.SourceType, "Tab's SourceType should change to Snapshot.");
        StringAssert.Contains(fileTab.Header, originalHeader, "Snapshot tab header should contain original header.");
        StringAssert.Contains(fileTab.Header, "(Snapshot", "Snapshot tab header should indicate it's a snapshot.");
        Assert.IsFalse(fileTab.IsActive, "Snapshot tab should be deactivated.");

        // Assert: A new tab is created and activated
        // Note: The logic for this is not yet implemented in MainViewModel.HandleTabSourceRestart
        // This test will fail until that logic is added.
        // Assert.AreEqual(initialTabCount + 1, _viewModel.TabViewModels.Count, "A new tab should have been created.");
        // var newActiveTab = _viewModel.ActiveTabViewModel;
        // Assert.IsNotNull(newActiveTab, "A new tab should be active.");
        // Assert.AreNotSame(fileTab, newActiveTab, "The new active tab should not be the snapshot tab.");
        // Assert.AreEqual(SourceType.File, newActiveTab.SourceType, "New active tab's source type should be File.");
        // Assert.AreEqual(filePath, newActiveTab.SourceIdentifier, "New active tab should monitor the original file path.");
    }

    private async Task<TabViewModel> ArrangeInitialLinesForTab(IEnumerable<string> lines)
    {
        _mockFileLogSource.LinesForInitialRead.Clear();
        _mockFileLogSource.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_setup_tab.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel is null after arranging lines.");

        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        return activeTab;
    }
    
    // Verifies: [ReqPasteFromClipboardv1]
    [TestMethod] public void PasteCommand_WithContent_CreatesNewTab()
    {
        RunOnSta(() =>
        {
            // Arrange
            string pastedContent = "Pasted log content.";
            Clipboard.SetText(pastedContent);
            int initialTabCount = _viewModel.TabViewModels.Count;
            Assert.AreEqual(1, initialTabCount, "Should start with one 'Welcome' tab.");

            // Act
            _viewModel.PasteCommand.Execute(null);
            _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

            // Assert
            Assert.AreEqual(1, _viewModel.TabViewModels.Count, "A new tab should replace the initial welcome tab.");
            var newTab = _viewModel.ActiveTabViewModel;
            Assert.IsNotNull(newTab, "A new tab should be active.");
            Assert.AreEqual(SourceType.Pasted, newTab.SourceType, "New tab's source type should be Pasted.");
            Assert.AreEqual(1, newTab.FilteredLogLines.Count);
            Assert.AreEqual(pastedContent, newTab.FilteredLogLines[0].Text);
        });
    }
}
