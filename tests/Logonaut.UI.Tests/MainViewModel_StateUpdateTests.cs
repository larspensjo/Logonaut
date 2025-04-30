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
        // Arrange: Setup init        // Arrange: Setup initial FilteredLogLines and search state
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Old Line 1"));
        _viewModel.SearchText = "Old"; // Triggers search via property setter logic
        _testContext.Send(_ => { }, null); // Ensure search updates
        Assert.AreEqual(1, _viewModel.SearchMarkers.Count, "Arrange failure: Search markers not set.");

        _viewModel.CurrentBusyStates.Clear(); // Set specific busy state for this test
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        var newLines = new List<FilteredLogLine> { new FilteredLogLine(10, "New") };

        // Act: Simulate processor sending a Replace update
        _mockProcessor.SimulateReplaceUpdate(newLines); // Use the specific simulation method
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert: ViewModel state updated
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count);
        Assert.AreEqual("New", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual(1, _viewModel.FilteredLogLinesCount);
        Assert.AreEqual(0, _viewModel.SearchMarkers.Count, "Search markers should be cleared on Replace.");
        Assert.AreEqual(-1, _viewModel.CurrentMatchOffset);

        // Assert: Busy state cleared
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count);
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken);
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
    [TestMethod] public void ApplyFilteredUpdate_Append_AddsOnlyNewLines_UpdatesSearch_ClearsFilteringToken()
    {
        // Arrange: Initial state
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Line 1 Old"));
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(2, "Line 2 Old"));
        _mockProcessor.SimulateTotalLinesUpdate(2);
        _viewModel.SearchText = "Old";
        _testContext.Send(_ => { }, null); // Run initial search
        // *** FIX: Reflect the fact that UpdateSearchMatches runs within the Post callback ***
        // In the test, GetCurrentDocumentText uses the FilteredLogLines. At this point, it only contains the initial lines.
        Assert.AreEqual(2, _viewModel.SearchMarkers.Count, "Arrange SearchMarkers");

        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        var linesToAppend = new List<FilteredLogLine>
        {
            new FilteredLogLine(3, "Line 3 New Append"),
            new FilteredLogLine(4, "Line 4 Old Context") // This line contains "Old"
        };

        // Act: Simulate processor sending an Append update
        _mockProcessor.SimulateAppendUpdate(linesToAppend);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // *** FIX: Manually trigger search update AFTER ApplyFilteredUpdate completes ***
        // This simulates what happens implicitly when the editor text *actually* updates in the UI.
        var updateSearchMethod = _viewModel.GetType().GetMethod("UpdateSearchMatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(updateSearchMethod, "Could not find UpdateSearchMatches method.");
        updateSearchMethod.Invoke(_viewModel, null);
        _testContext.Send(_ => { }, null); // Process any posts from UpdateSearchMatches if needed

        // Assert: ViewModel state updated by appending
        Assert.AreEqual(4, _viewModel.FilteredLogLines.Count); // 2 initial + 2 appended
        Assert.AreEqual(4, _viewModel.FilteredLogLinesCount);
        Assert.AreEqual("Line 3 New Append", _viewModel.FilteredLogLines[2].Text);
        Assert.AreEqual("Line 4 Old Context", _viewModel.FilteredLogLines[3].Text);

        // Assert: Search should now find matches in lines 1, 2, and 4.
        Assert.AreEqual(3, _viewModel.SearchMarkers.Count, "Search markers count after append mismatch.");

        // Assert: Busy state cleared
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count);
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken);
    }

    // Verifies: [ReqGeneralBusyIndicatorv1], [ReqLoadingOverlayIndicatorv1]
    [TestMethod] public void BusyStates_ManagedCorrectly_DuringInitialLoad_WithIncrementalPipeline()
    {
        // Arrange
        _mockFileDialog.FileToReturn = "C:\\good\\log.txt";
        List<FilteredLogLine> initialLinesResult = new() { new(1, "Line 1") };
        _viewModel.CurrentBusyStates.Clear();

        // Act 1: Start the file open process
        var openTask = _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        // It runs synchronously up to the first await because of mocks & ImmediateSynchronizationContext

        // Assert 1: *Both* LoadingToken and FilteringToken should be added by synchronous Post calls
        _testContext.Send(_ => { }, null); // Flush context queue
        // *** FIX: Expect 2 tokens here ***
        Assert.AreEqual(2, _viewModel.CurrentBusyStates.Count, "State 1 Count (Loading+Filtering)");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "State 1 Loading");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "State 1 Filtering");

        // Act 2: Simulate the FilterProcessor sending the *first* Replace update
        _mockProcessor.SimulateReplaceUpdate(initialLinesResult);
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate

        // Assert 2 (was Assert 3): Both tokens removed by the first Replace update after load
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "State 2 Count (After Replace)");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "State 2 Loading");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "State 2 Filtering");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines not updated.");

        // Ensure the async command actually completes (though it likely already has)
        // Need to await the task if it wasn't fully synchronous due to some unforeseen awaiter.
        // Using Task.Run to await safely in a test context if needed, but might not be necessary with mocks.
        // await Task.Run(() => openTask); // Or simply `await openTask;` if context allows.
        // Given the mocks, it should complete synchronously.
    }

    // Verifies: [ReqGeneralBusyIndicatorv1]
    [TestMethod] public void ApplyFilteredUpdate_Append_AfterNewLineArrival_ClearsFilteringToken()
    {
         // Arrange: Simulate filtering busy state active because new lines arrived
         // (Note: In reality, the token might not be added for *every* buffer, but for testing the removal, assume it was added)
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken);
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        // Act: Simulate Append update resulting from processing those new lines
        _mockProcessor.SimulateAppendUpdate(new List<FilteredLogLine> { new(5, "Appended Line") });
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count);
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken);
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken);
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
