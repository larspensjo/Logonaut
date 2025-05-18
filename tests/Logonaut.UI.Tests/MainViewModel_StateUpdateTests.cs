using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using System.Threading.Tasks;
using System;

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests focused on how the VM state changes in response to processor updates,
/// highlighting changes, and busy state management.
/// </summary>
[TestClass] public class MainViewModel_StateUpdateTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        base.TestInitialize();
        base.SetupMainAndTabViewModel();
    }

    private async Task SetupWithInitialLinesForTab(IEnumerable<string> lines, IFilter? initialFilter = null)
    {
        _mockFileLogSource.LinesForInitialRead.Clear();
        _mockFileLogSource.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_state_tab.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        if (initialFilter != null)
        {
            Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
            _viewModel.ActiveFilterProfile.SetModelRootFilter(initialFilter);
            InjectTriggerFilterUpdate();
            _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        }
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1], [ReqStatusBarFilteredLinesv1], [ReqSearchHighlightResultsv1] (Reset)
    [TestMethod] public async Task FilterChange_TriggersReplace_ClearsAndAddsLines_ResetsSearch_ClearsFilteringToken()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Old Line 1", "Match Me" });
        _tabViewModel.SearchText = "Old";
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count(l => l.Text.Contains("Old")), "Arrange FilteredLines count mismatch.");
        Assert.AreEqual(1, _tabViewModel.SearchMarkers.Count, "Arrange SearchMarkers count mismatch.");

        _tabViewModel.CurrentBusyStates.Clear();
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count);

        // Act
        _viewModel.ContextLines = 1;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Filtered line count after context change mismatch.");
        Assert.AreEqual("Old Line 1", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual("Match Me", _viewModel.FilteredLogLines[1].Text);
        Assert.AreEqual(2, _tabViewModel.FilteredLogLinesCount);
        Assert.AreEqual(1, _tabViewModel.SearchMarkers.Count, "Search markers should be 1 (for 'Old Line 1')."); // CHANGED
        Assert.AreEqual(CalculateExpectedOffset(0, "Old"), _tabViewModel.CurrentMatchOffset); // Verify selected match

        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's busy state should be cleared.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task FilterChange_TriggersReplace_RestoresHighlightBasedOnOriginalLineNumber()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> {
            "Line 5 Data",      // Orig 1
            "Line 10 Highlight",// Orig 2
            "Line 15 Info"      // Orig 3
        });
        _tabViewModel.HighlightedFilteredLineIndex = 1;
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch.");

        var filter = new SubstringFilter("Line"); // Matches all
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(filter);
        InjectTriggerFilterUpdate();
        // After this filter, line "Line 10 Highlight" (Orig 2) should still be at index 1
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Arrange: Highlight index mismatch after filter.");
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch after filter.");

        // Act
        _viewModel.ContextLines = 1; // This will add all lines again (as context to themselves if they are matches)
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(3, _viewModel.FilteredLogLines.Count, "Filtered lines count mismatch after replace.");
        int expectedNewIndex = _viewModel.FilteredLogLines
                                     .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                                     .FirstOrDefault(item => item.OriginalLineNumber == 2)?.Index ?? -1;

        Assert.AreEqual(expectedNewIndex, _tabViewModel.HighlightedFilteredLineIndex, "Highlight index not restored correctly to original line 2.");
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Original line number incorrect after restore.");
        Assert.AreEqual(1, expectedNewIndex, "Expected new index of original line 2 is wrong.");
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1] (append scenario), [ReqStatusBarFilteredLinesv1]
    [TestMethod] public async Task NewLineArrival_TriggersAppend_AddsOnlyNewLines_UpdatesSearch_ClearsFilteringToken()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Line 1 Old", "Line 2 Old" });
        _tabViewModel.SearchText = "Old";
        Assert.AreEqual(2, _tabViewModel.SearchMarkers.Count, "Arrange SearchMarkers");

        _tabViewModel.CurrentBusyStates.Clear();
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count);

        // Act
        var source = GetActiveMockSource();
        source.EmitLine("Line 3 New Append");
        source.EmitLine("Line 4 Old Context");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(4, _viewModel.FilteredLogLines.Count, "Append: Filtered lines count");
        Assert.AreEqual(4, _tabViewModel.FilteredLogLinesCount, "Append: TabViewModel.FilteredLogLinesCount");
        Assert.AreEqual("Line 3 New Append", _viewModel.FilteredLogLines[2].Text);
        Assert.AreEqual("Line 4 Old Context", _viewModel.FilteredLogLines[3].Text);
        Assert.AreEqual(3, _tabViewModel.SearchMarkers.Count, "Append: Search markers count mismatch.");
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

        // Assert 1: Check for TabViewModel's LoadingToken AND FilteringToken
        // TabViewModel.ActivateAsync adds LoadingToken.
        // Then ApplyFiltersFromProfile -> ReactiveStream.UpdateFilterSettings -> ApplyFilteredUpdateToThisTab (first replace) adds FilteringToken.
        Assert.AreEqual(2, _tabViewModel.CurrentBusyStates.Count, "Tab's busy token count mismatch early.");
        CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.LoadingToken, "Tab's LoadingToken missing early.");
        CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken, "Tab's FilteringToken missing early.");

        // Act 2: Allow processing to complete
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        await openTask;

        // Assert 2: Final state
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
        // Simulate that an update processing started
        _tabViewModel.CurrentBusyStates.Add(TabViewModel.FilteringToken);
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count);

        // Act
        GetActiveMockSource().EmitLine("Appended Line");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow append to process

        // Assert
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's FilteringToken not cleared after append.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken);
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetValid_UpdatesOriginalLineNumber()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Line 5", "Line 10" }); // Orig 1, 2

        // Act
        _tabViewModel.HighlightedFilteredLineIndex = 1;

        // Assert
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Filtered index mismatch.");
        Assert.AreEqual(2, _tabViewModel.HighlightedOriginalLineNumber, "Original number mismatch.");
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetInvalid_ResetsOriginalLineNumber()
    {
        // Arrange
        await SetupWithInitialLinesForTab(new List<string> { "Line 5" }); // Orig 1
        _tabViewModel.HighlightedFilteredLineIndex = 0;
        Assert.AreEqual(1, _tabViewModel.HighlightedOriginalLineNumber, "Arrange failure.");

        // Act: Set to -1
        _tabViewModel.HighlightedFilteredLineIndex = -1;
        // Assert
        Assert.AreEqual(-1, _tabViewModel.HighlightedFilteredLineIndex, "Filtered index should be -1.");
        Assert.AreEqual(-1, _tabViewModel.HighlightedOriginalLineNumber, "Original number should be -1.");

        // Act: Set out of bounds
        _tabViewModel.HighlightedFilteredLineIndex = 5;
        // Assert
        Assert.AreEqual(5, _tabViewModel.HighlightedFilteredLineIndex, "Filtered index should be 5."); // The property itself will hold the value
        Assert.AreEqual(-1, _tabViewModel.HighlightedOriginalLineNumber, "Original number should reset to -1 on invalid index.");
    }
}
