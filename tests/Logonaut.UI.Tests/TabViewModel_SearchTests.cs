using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Common;
using Logonaut.UI.ViewModels;
using System; // For StringComparison, Environment
using Logonaut.TestUtils;

namespace Logonaut.UI.Tests.ViewModels;

/*
 * Unit tests for the search functionality within the TabViewModel.
 * These tests verify text matching, case sensitivity, navigation through search results,
 * and the correct updating of search status and UI-bound properties for highlighting.
 */
[TestClass] public class TabViewModel_SearchTests : MainViewModelTestBase
{

    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();
        base.SetupMainAndTabViewModel(); // This sets up _viewModel and _tabViewModel
        _tabViewModel.FilteredLogLines.Clear(); // Ensure a clean slate for each test
    }

    // Verifies: [ReqSearchTextEntryv1], [ReqSearchHighlightResultsv1], [ReqSearchCaseSensitiveOptionv1], [ReqStatusBarSearchStatusv1]
    [TestMethod] public void SearchText_Set_UpdatesMatchesAndStatus_CaseSensitivity()
    {
        // Arrange
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line one with test"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Line two NO MATCH"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(3, "Line three with TEST"));

        // Act & Assert: Case Insensitive
        _tabViewModel.IsCaseSensitiveSearch = false;
        _tabViewModel.SearchText = "test";

        Assert.AreEqual(2, _tabViewModel.SearchMarkers.Count, "Case-insensitive marker count mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 1 of 2", "Case-insensitive status text mismatch.");
        Assert.AreEqual(CalculateExpectedOffset(0, "test"), _tabViewModel.SearchMarkers[0].Offset, "Case-insensitive offset 1 mismatch.");
        Assert.AreEqual(CalculateExpectedOffset(2, "TEST"), _tabViewModel.SearchMarkers[1].Offset, "Case-insensitive offset 2 mismatch.");

        // Act & Assert: Case Sensitive
        _tabViewModel.IsCaseSensitiveSearch = true;

        Assert.AreEqual(1, _tabViewModel.SearchMarkers.Count, "Case-sensitive marker count mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 1 of 1", "Case-sensitive status text mismatch.");
        Assert.AreEqual(CalculateExpectedOffset(0, "test"), _tabViewModel.SearchMarkers[0].Offset, "Case-sensitive offset mismatch.");
    }

    // Verifies: [ReqSearchNavigateResultsv1] (Next), [ReqHighlightSelectedLinev1] (Indirectly)
    [TestMethod] public void NextSearchCommand_CyclesThroughMatches_UpdatesSelectionAndHighlight()
    {
        // Arrange
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(3, "Other"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(4, "Test 3"));
        _tabViewModel.SearchText = "Test";
        Assert.AreEqual(3, _tabViewModel.SearchMarkers.Count);

        int expectedOffset1 = CalculateExpectedOffset(0, "Test");
        int expectedOffset2 = CalculateExpectedOffset(1, "Test");
        int expectedOffset3 = CalculateExpectedOffset(3, "Test");

        Assert.AreEqual(expectedOffset1, _tabViewModel.CurrentMatchOffset, "Initial selection after SearchText set.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 1 of 3", "Initial status.");

        // Act & Assert Cycle
        _tabViewModel.NextSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset2, _tabViewModel.CurrentMatchOffset, "Cycle 1 Offset mismatch.");
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Cycle 1 Highlight mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 2 of 3", "Cycle 1 Status mismatch.");

        _tabViewModel.NextSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset3, _tabViewModel.CurrentMatchOffset, "Cycle 2 Offset mismatch.");
        Assert.AreEqual(3, _tabViewModel.HighlightedFilteredLineIndex, "Cycle 2 Highlight mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 3 of 3", "Cycle 2 Status mismatch.");

        _tabViewModel.NextSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset1, _tabViewModel.CurrentMatchOffset, "Wrap Offset mismatch.");
        Assert.AreEqual(0, _tabViewModel.HighlightedFilteredLineIndex, "Wrap Highlight mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 1 of 3", "Wrap Status mismatch.");
    }

    // Verifies: [ReqSearchNavigateResultsv1] (Previous)
    [TestMethod] public void PreviousSearchCommand_CyclesThroughMatches_UpdatesSelectionAndHighlight()
    {
        // Arrange
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(3, "Other"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(4, "Test 3"));
        _tabViewModel.SearchText = "Test";
        Assert.AreEqual(3, _tabViewModel.SearchMarkers.Count);

        int expectedOffset1 = CalculateExpectedOffset(0, "Test");
        int expectedOffset2 = CalculateExpectedOffset(1, "Test");
        int expectedOffset3 = CalculateExpectedOffset(3, "Test");

        Assert.AreEqual(expectedOffset1, _tabViewModel.CurrentMatchOffset, "Initial selection after SearchText set.");

        // Act & Assert Cycle
        _tabViewModel.PreviousSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset3, _tabViewModel.CurrentMatchOffset, "Cycle 1 Offset mismatch.");
        Assert.AreEqual(3, _tabViewModel.HighlightedFilteredLineIndex, "Cycle 1 Highlight mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 3 of 3", "Cycle 1 Status mismatch.");

        _tabViewModel.PreviousSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset2, _tabViewModel.CurrentMatchOffset, "Cycle 2 Offset mismatch.");
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Cycle 2 Highlight mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 2 of 3", "Cycle 2 Status mismatch.");

        _tabViewModel.PreviousSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset1, _tabViewModel.CurrentMatchOffset, "Cycle 3 Offset mismatch.");
        Assert.AreEqual(0, _tabViewModel.HighlightedFilteredLineIndex, "Cycle 3 Highlight mismatch.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 1 of 3", "Cycle 3 Status mismatch.");
    }

    // Verifies: [ReqSearchNavigateResultsv1] (Next/CanExecute)
    [TestMethod] public void NextSearch_WithNonEmptySearchText_ShouldNavigate()
    {
        // Arrange
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2"));
        _tabViewModel.SearchText = "Test";

        // Act
        Assert.IsTrue(_tabViewModel.NextSearchCommand.CanExecute(null), "NextSearch should be enabled.");
        _tabViewModel.NextSearchCommand.Execute(null);

        // Assert
        Assert.AreEqual(CalculateExpectedOffset(1, "Test"), _tabViewModel.CurrentMatchOffset, "Should select the second match.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 2 of 2");
    }

    // Verifies: [ReqSearchNavigateResultsv1] (Previous/CanExecute)
    [TestMethod] public void PreviousSearch_WithNonEmptySearchText_ShouldNavigate()
    {
        // Arrange
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2"));
        _tabViewModel.SearchText = "Test";

        // Act
        Assert.IsTrue(_tabViewModel.PreviousSearchCommand.CanExecute(null), "PreviousSearch should be enabled.");
        _tabViewModel.PreviousSearchCommand.Execute(null);

        // Assert
        Assert.AreEqual(CalculateExpectedOffset(1, "Test"), _tabViewModel.CurrentMatchOffset, "Should select the second match on previous from start.");
        StringAssert.Contains(_tabViewModel.SearchStatusText, "Match 2 of 2");
    }
}
