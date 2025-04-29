using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Common;

namespace Logonaut.UI.Tests.ViewModels;

 /// Tests related to search text, navigation, case sensitivity, markers
[TestClass] public class MainViewModel_SearchTests : MainViewModelTestBase
{
    // Verifies: [ReqSearchTextEntryv1], [ReqSearchHighlightResultsv1], [ReqSearchStatusIndicatorv1],
    //           [ReqSearchCaseSensitiveOptionv1], [ReqSearchRulerMarkersv1]
    [TestMethod] public void SearchText_Set_UpdatesMatchesAndStatus_CaseSensitivity()
    {
        // Arrange
        _viewModel.FilteredLogLines.Clear();
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line one with test"));
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Line two NO MATCH"));
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(3, "Line three with TEST"));
        // Simulate internal text update
        var updateMethod = _viewModel.GetType().GetMethod("ReplaceLogTextInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        updateMethod?.Invoke(_viewModel, new object[] { _viewModel.FilteredLogLines.ToList() });

        // Act & Assert: Case Insensitive
        _viewModel.IsCaseSensitiveSearch = false;
        _viewModel.SearchText = "test";
        _testContext.Send(_ => { }, null); // Allow update

        Assert.AreEqual(2, _viewModel.SearchMarkers.Count, "Case-insensitive marker count mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "2 matches found", "Case-insensitive status text mismatch.");
        Assert.AreEqual(14, _viewModel.SearchMarkers[0].Offset, "Case-insensitive offset 1 mismatch.");
        Assert.AreEqual(55, _viewModel.SearchMarkers[1].Offset, "Case-insensitive offset 2 mismatch.");

        // Act & Assert: Case Sensitive
        _viewModel.IsCaseSensitiveSearch = true; // This triggers UpdateSearchMatches via its property changed handler
        _testContext.Send(_ => { }, null); // Allow update

        Assert.AreEqual(1, _viewModel.SearchMarkers.Count, "Case-sensitive marker count mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "1 matches found", "Case-sensitive status text mismatch.");
        Assert.AreEqual(14, _viewModel.SearchMarkers[0].Offset, "Case-sensitive offset mismatch.");
    }

    // Verifies: [ReqSearchNavigateResultsv1] (Next), [ReqHighlightSelectedLinev1] (Indirectly)
    [TestMethod] public void NextSearchCommand_CyclesThroughMatches_UpdatesSelectionAndHighlight()
    {
        // Arrange
        _viewModel.FilteredLogLines.Clear();
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1")); // Index 0
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2")); // Index 1
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(3, "Other"));
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(4, "Test 3")); // Index 3 (in collection)
        // Simulate internal text update
        var updateMethod = _viewModel.GetType().GetMethod("ReplaceLogTextInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        updateMethod?.Invoke(_viewModel, new object[] { _viewModel.FilteredLogLines.ToList() });
        _viewModel.SearchText = "Test"; // Triggers initial search
        _testContext.Send(_ => { }, null); // Ensure search runs
        Assert.AreEqual(3, _viewModel.SearchMarkers.Count);

        int expectedOffset1 = CalculateExpectedOffset(0, "Test");
        int expectedOffset2 = CalculateExpectedOffset(1, "Test");
        int expectedOffset3 = CalculateExpectedOffset(3, "Test");

        // Act & Assert Cycle
        _viewModel.NextSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset1, _viewModel.CurrentMatchOffset, "Cycle 1 Offset mismatch.");
        Assert.AreEqual(0, _viewModel.HighlightedFilteredLineIndex, "Cycle 1 Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 1 of 3", "Cycle 1 Status mismatch.");

        _viewModel.NextSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset2, _viewModel.CurrentMatchOffset, "Cycle 2 Offset mismatch.");
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Cycle 2 Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 2 of 3", "Cycle 2 Status mismatch.");

        _viewModel.NextSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset3, _viewModel.CurrentMatchOffset, "Cycle 3 Offset mismatch.");
        Assert.AreEqual(3, _viewModel.HighlightedFilteredLineIndex, "Cycle 3 Highlight mismatch."); // Corresponds to the item at index 3
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 3 of 3", "Cycle 3 Status mismatch.");

        _viewModel.NextSearchCommand.Execute(null); // Wrap
        Assert.AreEqual(expectedOffset1, _viewModel.CurrentMatchOffset, "Wrap Offset mismatch.");
        Assert.AreEqual(0, _viewModel.HighlightedFilteredLineIndex, "Wrap Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 1 of 3", "Wrap Status mismatch.");
    }

    // Verifies: [ReqSearchNavigateResultsv1] (Previous)
    [TestMethod] public void PreviousSearchCommand_CyclesThroughMatches_UpdatesSelectionAndHighlight()
    {
        // Arrange
        _viewModel.FilteredLogLines.Clear();
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1")); // Index 0
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2")); // Index 1
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(3, "Other"));
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(4, "Test 3")); // Index 3
        // Simulate internal text update
        var updateMethod = _viewModel.GetType().GetMethod("ReplaceLogTextInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        updateMethod?.Invoke(_viewModel, new object[] { _viewModel.FilteredLogLines.ToList() });
        _viewModel.SearchText = "Test";
        _testContext.Send(_ => { }, null);
        Assert.AreEqual(3, _viewModel.SearchMarkers.Count);

        int expectedOffset1 = CalculateExpectedOffset(0, "Test");
        int expectedOffset2 = CalculateExpectedOffset(1, "Test");
        int expectedOffset3 = CalculateExpectedOffset(3, "Test");

        // Act & Assert Cycle
        _viewModel.PreviousSearchCommand.Execute(null); // Wrap to last
        Assert.AreEqual(expectedOffset3, _viewModel.CurrentMatchOffset, "Cycle 1 Offset mismatch.");
        Assert.AreEqual(3, _viewModel.HighlightedFilteredLineIndex, "Cycle 1 Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 3 of 3", "Cycle 1 Status mismatch.");

        _viewModel.PreviousSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset2, _viewModel.CurrentMatchOffset, "Cycle 2 Offset mismatch.");
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Cycle 2 Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 2 of 3", "Cycle 2 Status mismatch.");

        _viewModel.PreviousSearchCommand.Execute(null);
        Assert.AreEqual(expectedOffset1, _viewModel.CurrentMatchOffset, "Cycle 3 Offset mismatch.");
        Assert.AreEqual(0, _viewModel.HighlightedFilteredLineIndex, "Cycle 3 Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 1 of 3", "Cycle 3 Status mismatch.");

        _viewModel.PreviousSearchCommand.Execute(null); // Wrap to last
        Assert.AreEqual(expectedOffset3, _viewModel.CurrentMatchOffset, "Wrap Offset mismatch.");
        Assert.AreEqual(3, _viewModel.HighlightedFilteredLineIndex, "Wrap Highlight mismatch.");
        StringAssert.Contains(_viewModel.SearchStatusText, "Match 3 of 3", "Wrap Status mismatch.");
    }

     // Verifies: [ReqSearchNavigateResultsv1] (Next/CanExecute)
    [TestMethod] public void NextSearch_WithNonEmptySearchText_ShouldNavigate() // Kept from original
    {
         // Arrange
         _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1"));
         _viewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2"));
         // Simulate internal text update
         var updateMethod = _viewModel.GetType().GetMethod("ReplaceLogTextInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
         updateMethod?.Invoke(_viewModel, new object[] { _viewModel.FilteredLogLines.ToList() });
         _viewModel.SearchText = "Test";
         _testContext.Send(_ => { }, null); // Allow UpdateSearchMatches
         int initialOffset = _viewModel.CurrentMatchOffset;

         // Act
         Assert.IsTrue(_viewModel.NextSearchCommand.CanExecute(null), "NextSearch should be enabled.");
         _viewModel.NextSearchCommand.Execute(null); // Should go to first match

         // Assert
         Assert.AreNotEqual(initialOffset, _viewModel.CurrentMatchOffset, "CurrentMatchOffset should change.");
         Assert.AreEqual(0, _viewModel.CurrentMatchOffset, "Should select the first match.");
         StringAssert.Contains(_viewModel.SearchStatusText, "Match 1 of 2");
    }

     // Verifies: [ReqSearchNavigateResultsv1] (Previous/CanExecute)
    [TestMethod] public void PreviousSearch_WithNonEmptySearchText_ShouldNavigate() // Kept from original
    {
         // Arrange
         _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test 1"));
         _viewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Test 2"));
          // Simulate internal text update
         var updateMethod = _viewModel.GetType().GetMethod("ReplaceLogTextInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
         updateMethod?.Invoke(_viewModel, new object[] { _viewModel.FilteredLogLines.ToList() });
         _viewModel.SearchText = "Test";
         _testContext.Send(_ => { }, null); // Allow UpdateSearchMatches
         int initialOffset = _viewModel.CurrentMatchOffset;

         // Act
         Assert.IsTrue(_viewModel.PreviousSearchCommand.CanExecute(null), "PreviousSearch should be enabled.");
         _viewModel.PreviousSearchCommand.Execute(null); // Should wrap to last match

         // Assert
         Assert.AreNotEqual(initialOffset, _viewModel.CurrentMatchOffset, "CurrentMatchOffset should change.");
         StringAssert.Contains(_viewModel.SearchStatusText, "Match 2 of 2"); // Assuming 2 matches
    }
}
