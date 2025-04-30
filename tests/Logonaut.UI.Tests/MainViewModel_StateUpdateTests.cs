using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.UI;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI.Tests.ViewModels;

 /// Tests focused on how the VM state changes in response to processor updates, highlighting, busy states
[TestClass] public class MainViewModel_StateUpdateTests : MainViewModelTestBase
{
    // Verifies: [ReqDisplayRealTimeUpdatev1], [ReqStatusBarFilteredLinesv1]
    [TestMethod] public void ApplyFilteredUpdate_Replace_ClearsAndAddsLines_ResetsSearch_ClearsFilteringToken()
    {
        // Arrange: Setup initial FilteredLogLines and search state
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Old Line 1"));
        _viewModel.SearchText = "Old";
        _testContext.Send(_ => { }, null); // Let search run (via UpdateSearchMatches called from setter)
        Assert.AreEqual(1, _viewModel.SearchMarkers.Count, "Arrange failure: Search markers not set.");

        // Arrange: Explicitly set the busy state for *this test scenario*
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "Arrange failure: Expected 1 busy state token.");

        var newLines = new List<FilteredLogLine> { new FilteredLogLine(10, "New") };

        // Act: Simulate the processor sending the update
        _mockProcessor.SimulateReplaceUpdate(newLines);
        _testContext.Send(_ => { }, null); // Flushes queue, runs ApplyFilteredUpdate logic

        // Assert: ViewModel state updated
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "FilteredLogLines count mismatch.");
        Assert.AreEqual("New", _viewModel.FilteredLogLines[0].Text, "FilteredLogLines content mismatch.");
        Assert.AreEqual(1, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount property mismatch.");
        Assert.AreEqual(0, _viewModel.SearchMarkers.Count, "Search markers should be cleared on Replace.");
        Assert.AreEqual(-1, _viewModel.CurrentMatchOffset, "Current match offset should be reset.");

        // Assert: Busy state cleared (FilteringToken removed)
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy states should be empty after Replace.");
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public void ApplyFilteredUpdate_Replace_RestoresHighlightBasedOnOriginalLineNumber()
    {
        // Arrange
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(5, "Line Five"));
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(10, "Line Ten"));    // Index 1
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(15, "Line Fifteen"));
        _viewModel.HighlightedFilteredLineIndex = 1; // Select "Line Ten"
        Assert.AreEqual(10, _viewModel.HighlightedOriginalLineNumber, "Arrange failure: Original line number mismatch.");

        // New list where original line 10 is now at index 0
        var newLines = new List<FilteredLogLine> { new(10, "Ten"), new(20, "Twenty") };

        // Act
        _mockProcessor.SimulateReplaceUpdate(newLines);
        _testContext.Send(_ => { }, null); // Process update and highlight restore

        // Assert
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Filtered lines count mismatch.");
        Assert.AreEqual(0, _viewModel.HighlightedFilteredLineIndex, "Highlight index not restored correctly.");
        Assert.AreEqual(10, _viewModel.HighlightedOriginalLineNumber, "Original line number incorrect after restore.");
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1] (append scenario), [ReqStatusBarFilteredLinesv1]
    [TestMethod] public void ApplyFilteredUpdate_Append_AddsLines_UpdatesSearch_ClearsFilteringToken()
    {
        // Arrange: Simulate initial state
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line 1 Old"));
        _mockProcessor.SimulateTotalLinesUpdate(1); // Simulate initial count
        _testContext.Send(_ => {}, null);

        // Arrange: Simulate Filtering busy state
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        // Arrange: Define lines that represent an append
        var appendedLines = new List<FilteredLogLine>
        {
            new FilteredLogLine(1, "Line 1 Old"), // Existing line must match
            new FilteredLogLine(2, "Line 2 New Append") // New line
        };

        // Act: Simulate processor sending update
        _mockProcessor.SimulateReplaceUpdate(appendedLines);
        _testContext.Send(_ => { }, null); // Process the update

        // Assert: ViewModel state updated
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
        Assert.AreEqual(2, _viewModel.FilteredLogLinesCount);
        Assert.AreEqual("Line 2 New Append", _viewModel.FilteredLogLines[1].Text);

        // Assert: Busy state cleared
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count);
    }

    // Verifies: [ReqGeneralBusyIndicatorv1], [ReqLoadingOverlayIndicatorv1]
    [TestMethod] public void BusyStates_ManagedCorrectly_DuringInitialLoad()
    {
        // Arrange
        _mockFileDialog.FileToReturn = "C:\\good\\log.txt";
        List<FilteredLogLine> initialLines = new() { new(1, "Line 1") };
        _viewModel.CurrentBusyStates.Clear(); // Ensure start empty for test clarity

        // Act 1: Start the file open process (don't await yet)
        var openTask = _viewModel.OpenLogFileCommand.ExecuteAsync(null);

        // Assert 1: LoadingToken and FilteringToken added
        _testContext.Send(_ => { }, null); // Flush context queue
        Assert.AreEqual(2, _viewModel.CurrentBusyStates.Count, "Busy state count after OpenLogFile start incorrect.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken missing after start.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken missing after start.");

        // Act 2: Simulate initial read completion (part of await ExecuteAsync does this)
        // Assert 2: State remains the same (waiting for processor's first update)
        Assert.AreEqual(2, _viewModel.CurrentBusyStates.Count, "Busy state count after tailer read incorrect.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken missing after read.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken missing after read.");

        // Act 3: Simulate the FilterProcessor sending the *first* Replace update
        _mockProcessor.SimulateReplaceUpdate(initialLines);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert 3: Both tokens removed by the first Replace update after load
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy state count should be 0 after first Replace.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken not removed.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken not removed.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines not updated.");
    }

    // Verifies: [ReqGeneralBusyIndicatorv1]
    [TestMethod] public void ApplyFilteredUpdate_Replace_AfterManualFilterChange_ClearsFilteringToken()
    {
        // Arrange: Simulate filtering busy state *after* initial load
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken); // Only filtering is active
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        // Act
        _mockProcessor.SimulateReplaceUpdate(new List<FilteredLogLine> { new(1, "New") });
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy states should be empty after Replace.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken not removed.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken should not have been present.");
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public void HighlightedFilteredLineIndex_SetValid_UpdatesOriginalLineNumber()
    {
        // Arrange
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(5, "Line Five")); // Index 0
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(10, "Line Ten")); // Index 1

        // Act
        _viewModel.HighlightedFilteredLineIndex = 1;

        // Assert
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Filtered index mismatch.");
        Assert.AreEqual(10, _viewModel.HighlightedOriginalLineNumber, "Original number mismatch.");
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public void HighlightedFilteredLineIndex_SetInvalid_ResetsOriginalLineNumber()
    {
        // Arrange
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(5, "Line Five")); // Index 0
        _viewModel.HighlightedFilteredLineIndex = 0;
        Assert.AreEqual(5, _viewModel.HighlightedOriginalLineNumber, "Arrange failure.");

        // Act: Set to -1
        _viewModel.HighlightedFilteredLineIndex = -1;
        // Assert
        Assert.AreEqual(-1, _viewModel.HighlightedFilteredLineIndex, "Filtered index should be -1.");
        Assert.AreEqual(-1, _viewModel.HighlightedOriginalLineNumber, "Original number should be -1.");

        // Act: Set out of bounds
        _viewModel.HighlightedFilteredLineIndex = 5; // Out of bounds (count is 1)
        // Assert
        Assert.AreEqual(5, _viewModel.HighlightedFilteredLineIndex, "Filtered index should be 5.");
        Assert.AreEqual(-1, _viewModel.HighlightedOriginalLineNumber, "Original number should reset to -1 on invalid index.");
    }
}
