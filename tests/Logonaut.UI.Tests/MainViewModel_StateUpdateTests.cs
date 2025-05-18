using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using System.Threading.Tasks;
using System;

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
        base.SetupMainAndTabViewModel(); // Sets up _viewModel and _tabViewModel
    }

    /*
     * Helper to setup the internal TabViewModel with initial lines loaded via OpenLogFile simulation.
     * Ensures the TabViewModel's LogDataProcessor is correctly initialized.
     */
    private async Task SetupWithInitialLinesForTab(IEnumerable<string> lines, IFilter? initialFilter = null)
    {
        _mockFileLogSource.LinesForInitialRead.Clear();
        _mockFileLogSource.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_state_tab.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow LogDataProcessor to activate

        if (initialFilter != null)
        {
            Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
            _viewModel.ActiveFilterProfile.SetModelRootFilter(initialFilter);
            InjectTriggerFilterUpdate(); // Calls TabViewModel.ApplyFiltersFromProfile
            _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow filter application
        }
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1], [ReqStatusBarFilteredLinesv1], [ReqSearchHighlightResultsv1] (Reset)
    [TestMethod] public async Task FilterChange_TriggersReplace_ClearsAndAddsLines_ResetsSearch_ClearsFilteringToken()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Old Line 1", "Match Me" });
        _tabViewModel.SearchText = "Old"; // Search is on TabViewModel
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // For search
        Assert.AreEqual(1, _tabViewModel.FilteredLogLines.Count(l => l.Text.Contains("Old")), "Arrange FilteredLogLines count mismatch.");
        Assert.AreEqual(1, _tabViewModel.SearchMarkers.Count, "Arrange SearchMarkers count mismatch.");

        _tabViewModel.CurrentBusyStates.Clear();
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken); // Simulate start of filtering
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count);

        // Act
        _viewModel.ContextLines = 1; // Triggers filter update in MainViewModel -> TabViewModel -> LogDataProcessor
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow processing

        // Assert
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Filtered line count (delegated) after context change mismatch.");
        Assert.AreEqual("Old Line 1", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual("Match Me", _viewModel.FilteredLogLines[1].Text);
        Assert.AreEqual(2, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount mismatch.");

        // SearchMarkers should update because FilteredLogLines content changed.
        // "Old" is still present in "Old Line 1". "Match Me" is now included due to context.
        Assert.AreEqual(1, _tabViewModel.SearchMarkers.Count, "Search markers should be 1 (for 'Old Line 1').");
        Assert.AreEqual(CalculateExpectedOffset(0, "Old"), _tabViewModel.CurrentMatchOffset, "Selected search match offset is incorrect.");

        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy state should be cleared.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task FilterChange_TriggersReplace_RestoresHighlightBasedOnOriginalLineNumber()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> {
            "Line 5 Data",      // Orig 1 in mock source, but LogDataProcessor will re-number from 1
            "Line 10 Highlight",// Orig 2
            "Line 15 Info"      // Orig 3
        });
        // After setup, original line numbers in FilteredLogLines are 1, 2, 3.
        _tabViewModel.HighlightedFilteredLineIndex = 1; // Selects "Line 10 Highlight" (OriginalLineNumber 2)
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch.");

        var filter = new SubstringFilter("Line"); // Matches all three lines
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(filter);
        InjectTriggerFilterUpdate(); // Calls TabViewModel.ApplyFiltersFromProfile
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // After this filter, "Line 10 Highlight" (OriginalLineNumber 2) should still be at filtered index 1
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Arrange: Highlight index mismatch after filter.");
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch after filter.");

        // Act
        _viewModel.ContextLines = 1; // This will change how many lines are shown (or their context status)
                                     // but the original line numbers remain.
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow filter application

        // Assert
        // With filter "Line" and context 1, all 3 lines are still direct matches.
        // The relative order of original lines 1,2,3 should be preserved.
        Assert.AreEqual(3, _viewModel.FilteredLogLines.Count, "Filtered lines count mismatch after replace.");
        int expectedNewIndex = _viewModel.FilteredLogLines
                                     .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                                     .FirstOrDefault(item => item.OriginalLineNumber == 2)?.Index ?? -1;

        Assert.AreEqual(expectedNewIndex, _tabViewModel.HighlightedFilteredLineIndex, "Highlight index not restored correctly to original line 2.");
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Original line number incorrect after restore.");
        Assert.AreEqual(1, expectedNewIndex, "Expected new index of original line 2 is wrong."); // Should still be index 1
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1] (append scenario), [ReqStatusBarFilteredLinesv1]
    [TestMethod] public async Task NewLineArrival_TriggersAppend_AddsOnlyNewLines_UpdatesSearch_ClearsFilteringToken()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Line 1 Old", "Line 2 Old" });
        _tabViewModel.SearchText = "Old";
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // For search
        Assert.AreEqual(2, _tabViewModel.SearchMarkers.Count, "Arrange SearchMarkers");

        _tabViewModel.CurrentBusyStates.Clear();
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken); // Simulate start of filtering
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count);

        // Act
        var source = GetActiveMockSource();
        source.EmitLine("Line 3 New Append"); // New line, will be OriginalLineNumber 3
        source.EmitLine("Line 4 Old Context"); // New line, will be OriginalLineNumber 4, matches "Old"
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow append and search update

        // Assert
        Assert.AreEqual(4, _viewModel.FilteredLogLines.Count, "Append: Filtered lines count (delegated)");
        Assert.AreEqual(4, _tabViewModel.FilteredLogLinesCount, "Append: TabViewModel.FilteredLogLinesCount");
        Assert.AreEqual("Line 3 New Append", _viewModel.FilteredLogLines[2].Text);
        Assert.AreEqual("Line 4 Old Context", _viewModel.FilteredLogLines[3].Text);
        Assert.AreEqual(3, _tabViewModel.SearchMarkers.Count, "Append: Search markers count mismatch (should find 3 'Old's).");
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Append: Tab's busy state count");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken);
    }

    // Verifies: [ReqLoadingOverlayIndicatorv1]
    [TestMethod] public async Task OpenLogFile_ManagesTabBusyStatesCorrectly_DuringInitialLoad()
    {
        // Arrange
        _mockFileDialog.FileToReturn = "C:\\good\\log.txt";
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Line 1" };
        _tabViewModel.CurrentBusyStates.Clear();

        // Act 1: Start the command
        var openTask = _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        // At this point, MainViewModel calls TabViewModel.ActivateAsync, which calls Processor.ActivateAsync.
        // Processor.ActivateAsync sets _isInitialLoadInProgress = true and calls UpdateFilterSettings.
        // UpdateFilterSettings triggers the full refilter pipeline.
        // Before _backgroundScheduler.AdvanceBy, the pipeline may not have completed.
        // TabViewModel.ActivateAsync adds LoadingToken.
        // The first ReplaceFilteredUpdate from the stream (via TabVM.ApplyFilteredUpdateToThisTab) adds FilteringToken.

        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Minimal advance to let tasks start

        // Assert 1: Check for TabViewModel's LoadingToken. FilteringToken might appear slightly later.
        CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.LoadingToken, "Tab's LoadingToken missing early.");
        // Depending on TestScheduler timing, FilteringToken might or might not be present yet.
        // If it is, it's okay.
        if (_tabViewModel.CurrentBusyStates.Count > 1)
        {
            CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken, "Tab's FilteringToken expected if >1 token.");
        }


        // Act 2: Allow processing to complete
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        await openTask; // Ensure the OpenLogFileCommand task itself has completed

        // Assert 2: Final state
        // After initial load, ReactiveFilteredLogStream sends ReplaceFilteredUpdate with IsInitialLoadProcessingComplete=true.
        // TabViewModel.ApplyFilteredUpdateToThisTab removes LoadingToken. It also adds/removes FilteringToken.
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy state count after load mismatch.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.LoadingToken, "Tab's LoadingToken not cleared.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken, "Tab's FilteringToken not cleared.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines not updated after load.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "Global busy states should be empty.");
    }

    // Verifies: [ReqGeneralBusyIndicatorv1]
    [TestMethod] public async Task NewLineArrival_ClearsFilteringToken_AfterAppendProcessed()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Initial" });
        _tabViewModel.CurrentBusyStates.Clear();
        // Simulate that an update processing started by manually adding the token
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count);

        // Act
        GetActiveMockSource().EmitLine("Appended Line");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow append to process

        // Assert
        // TabViewModel.ApplyFilteredUpdateToThisTab adds FilteringToken at start and removes at end.
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's FilteringToken not cleared after append.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken);
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetValid_UpdatesOriginalLineNumber()
    {
        // Arrange
        // LogDataProcessor re-numbers lines from 1 for each new source.
        await SetupWithInitialLinesForTab(new List<string> { "Line X", "Line Y" }); // Orig 1, Orig 2 from processor's perspective

        // Act
        _tabViewModel.HighlightedFilteredLineIndex = 1; // Selects "Line Y"

        // Assert
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Filtered index mismatch.");
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Original number mismatch (should be 2).");
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetInvalid_ResetsOriginalLineNumber()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Line X" }); // Orig 1
        _tabViewModel.HighlightedFilteredLineIndex = 0;
        Assert.AreEqual(1, _tabViewModel.HighlightedOriginalLineNumber, "Arrange failure: Original number should be 1.");

        // Act: Set to -1
        _tabViewModel.HighlightedFilteredLineIndex = -1;
        // Assert
        Assert.AreEqual(-1, _tabViewModel.HighlightedFilteredLineIndex, "Filtered index should be -1.");
        Assert.AreEqual(-1, _tabViewModel.HighlightedOriginalLineNumber, "Original number should be -1.");

        // Act: Set out of bounds
        _tabViewModel.HighlightedFilteredLineIndex = 5;
        // Assert
        Assert.AreEqual(5, _tabViewModel.HighlightedFilteredLineIndex, "Filtered index should be 5.");
        Assert.AreEqual(-1, _tabViewModel.HighlightedOriginalLineNumber, "Original number should reset to -1 on invalid index.");
    }
}
