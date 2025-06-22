using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using System.Threading.Tasks;
using System;
using Logonaut.TestUtils;

namespace Logonaut.UI.Tests.ViewModels;

/*
 * Unit tests for MainViewModel and its internal TabViewModel focusing on state updates.
 * This includes responses to filter changes, new log line arrivals,
 * management of selection highlights, and busy state indicators during processing.
 * Tests ensure that the ViewModels correctly reflect the underlying data and processing status.
 */
[TestClass] public class MainViewModel_StateUpdateTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();
        base.SetupMainViewModel();
    }

    private async Task<TabViewModel> SetupWithInitialLinesForTab(IEnumerable<string> lines, IFilter? initialFilter = null)
    {
        // Arrange
        _mockFileLogSource.LinesForInitialRead.Clear();
        _mockFileLogSource.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_state_tab.log";

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel should not be null after opening a file.");

        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        if (initialFilter != null)
        {
            Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
            _viewModel.ActiveFilterProfile.SetModelRootFilter(initialFilter);
            InjectTriggerFilterUpdate();
            _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        }
        return activeTab;
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1], [ReqStatusBarFilteredLinesv1], [ReqSearchHighlightResultsv1]
    [TestMethod] public async Task FilterChange_TriggersReplace_ClearsAndAddsLines_ResetsSearch_ClearsFilteringToken()
    {
        // Arrange
        var tabViewModel = await SetupWithInitialLinesForTab(new List<string> { "Old Line 1", "Match Me" });
        tabViewModel.SearchText = "Old";
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count, "Arrange FilteredLogLines count mismatch.");
        Assert.AreEqual(1, tabViewModel.SearchMarkers.Count, "Arrange SearchMarkers count mismatch.");
        tabViewModel.CurrentBusyStates.Clear();

        // Act
        _viewModel.ContextLines = 1; // This triggers the filter update
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count, "Filtered line count after context change mismatch.");
        Assert.AreEqual("Old Line 1", tabViewModel.FilteredLogLines[0].Text);
        Assert.AreEqual("Match Me", tabViewModel.FilteredLogLines[1].Text);
        Assert.AreEqual(2, tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount mismatch.");
        Assert.AreEqual(1, tabViewModel.SearchMarkers.Count, "Search markers should be 1 (for 'Old Line 1').");
        Assert.AreEqual(CalculateExpectedOffset(0, "Old"), tabViewModel.CurrentMatchOffset, "Selected search match offset is incorrect.");
        Assert.AreEqual(0, tabViewModel.CurrentBusyStates.Count, "Tab's busy state should be cleared.");
        CollectionAssert.DoesNotContain(tabViewModel.CurrentBusyStates.ToList(), TabViewModel.FilteringToken);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task FilterChange_TriggersReplace_RestoresHighlightBasedOnOriginalLineNumber()
    {
        // Arrange
        var tabViewModel = await SetupWithInitialLinesForTab(new List<string> {
            "Line 5 Data",       // OriginalLineNumber will be 1
            "Line 10 Highlight", // OriginalLineNumber will be 2
            "Line 15 Info"       // OriginalLineNumber will be 3
        });
        tabViewModel.HighlightedFilteredLineIndex = 1;
        Assert.AreEqual(2, tabViewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch.");
        var filter = new SubstringFilter("Line");
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(filter);
        InjectTriggerFilterUpdate();
        Assert.AreEqual(3, tabViewModel.FilteredLogLines.Count);
        Assert.AreEqual(1, tabViewModel.HighlightedFilteredLineIndex, "Arrange: Highlight index mismatch after filter.");
        Assert.AreEqual(2, tabViewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch after filter.");

        // Act
        _viewModel.ContextLines = 1;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(3, tabViewModel.FilteredLogLines.Count, "Filtered lines count mismatch after replace.");
        int expectedNewIndex = tabViewModel.FilteredLogLines
                                     .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                                     .FirstOrDefault(item => item.OriginalLineNumber == 2)?.Index ?? -1;
        Assert.AreEqual(expectedNewIndex, tabViewModel.HighlightedFilteredLineIndex, "Highlight index not restored correctly to original line 2.");
        Assert.AreEqual(2, tabViewModel.HighlightedOriginalLineNumber, "Original line number incorrect after restore.");
        Assert.AreEqual(1, expectedNewIndex, "Expected new index of original line 2 is wrong.");
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1], [ReqStatusBarFilteredLinesv1]
    [TestMethod] public async Task NewLineArrival_TriggersAppend_AddsOnlyNewLines_UpdatesSearch_ClearsFilteringToken()
    {
        // Arrange
        var tabViewModel = await SetupWithInitialLinesForTab(new List<string> { "Line 1 Old", "Line 2 Old" });
        tabViewModel.SearchText = "Old";
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        Assert.AreEqual(2, tabViewModel.SearchMarkers.Count, "Arrange SearchMarkers");
        tabViewModel.CurrentBusyStates.Clear();

        // Act
        var source = GetActiveMockSource();
        source.EmitLine("Line 3 New Append");
        source.EmitLine("Line 4 Old Context");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(4, tabViewModel.FilteredLogLines.Count, "Append: Filtered lines count");
        Assert.AreEqual(4, tabViewModel.FilteredLogLinesCount, "Append: TabViewModel.FilteredLogLinesCount");
        Assert.AreEqual("Line 3 New Append", tabViewModel.FilteredLogLines[2].Text);
        Assert.AreEqual("Line 4 Old Context", tabViewModel.FilteredLogLines[3].Text);
        Assert.AreEqual(3, tabViewModel.SearchMarkers.Count, "Append: Search markers count mismatch (should find 3 'Old's).");
        Assert.AreEqual(0, tabViewModel.CurrentBusyStates.Count, "Append: Tab's busy state count");
        CollectionAssert.DoesNotContain(tabViewModel.CurrentBusyStates.ToList(), TabViewModel.FilteringToken);
    }

    // Verifies: [ReqLoadingOverlayIndicatorv1]
    [TestMethod] public async Task OpenLogFile_ManagesTabBusyStatesCorrectly_DuringInitialLoad()
    {
        // Arrange
        _mockFileDialog.FileToReturn = "C:\\good\\log.txt";
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Line 1" };

        // Act
        var openTask = _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "Active tab is null immediately after command execution.");

        // Assert (early state)
        // Immediately after command, state might be in flux, but let's check.
        // The LoadingToken should be present.
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks);
        Assert.IsTrue(activeTab.CurrentBusyStates.Any(), "Tab's busy state should not be empty early.");
        CollectionAssert.Contains(activeTab.CurrentBusyStates.ToList(), TabViewModel.LoadingToken, "Tab's LoadingToken missing early.");

        // Act (completion)
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        await openTask;

        // Assert (final state)
        Assert.AreEqual(0, activeTab.CurrentBusyStates.Count, "Tab's busy state count after load mismatch.");
        Assert.AreEqual(1, activeTab.FilteredLogLines.Count, "Filtered lines not updated after load.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states should be empty.");
    }

    // Verifies: [ReqGeneralBusyIndicatorv1]
    [TestMethod] public async Task NewLineArrival_ClearsFilteringToken_AfterAppendProcessed()
    {
        // Arrange
        var tabViewModel = await SetupWithInitialLinesForTab(new List<string> { "Initial" });
        tabViewModel.CurrentBusyStates.Clear();

        // Act
        var source = GetActiveMockSource();
        // Manually add the token to simulate the start of processing an append
        tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);
        source.EmitLine("Appended Line");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(0, tabViewModel.CurrentBusyStates.Count, "Tab's FilteringToken not cleared after append.");
        Assert.AreEqual(2, tabViewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetValid_UpdatesOriginalLineNumber()
    {
        // Arrange
        var tabViewModel = await SetupWithInitialLinesForTab(new List<string> { "Line X", "Line Y" });

        // Act
        tabViewModel.HighlightedFilteredLineIndex = 1;

        // Assert
        Assert.AreEqual(1, tabViewModel.HighlightedFilteredLineIndex, "Filtered index mismatch.");
        Assert.AreEqual(2, tabViewModel.HighlightedOriginalLineNumber, "Original number mismatch (should be 2).");
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetInvalid_ResetsOriginalLineNumber()
    {
        // Arrange
        var tabViewModel = await SetupWithInitialLinesForTab(new List<string> { "Line X" });
        tabViewModel.HighlightedFilteredLineIndex = 0;
        Assert.AreEqual(1, tabViewModel.HighlightedOriginalLineNumber, "Arrange failure: Original number should be 1.");

        // Act
        tabViewModel.HighlightedFilteredLineIndex = -1;
        
        // Assert
        Assert.AreEqual(-1, tabViewModel.HighlightedFilteredLineIndex, "Filtered index should be -1.");
        Assert.AreEqual(-1, tabViewModel.HighlightedOriginalLineNumber, "Original number should be -1.");

        // Act
        tabViewModel.HighlightedFilteredLineIndex = 5; // An out-of-bounds index
        
        // Assert
        Assert.AreEqual(5, tabViewModel.HighlightedFilteredLineIndex, "Filtered index should be 5.");
        Assert.AreEqual(-1, tabViewModel.HighlightedOriginalLineNumber, "Original number should reset to -1 on invalid index.");
    }
}
