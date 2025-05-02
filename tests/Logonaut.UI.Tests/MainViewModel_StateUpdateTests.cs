// tests/Logonaut.UI.Tests/MainViewModel_StateUpdateTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Filters; // Ensure the correct namespace for IFilter is imported
using Logonaut.UI.ViewModels;
using System.Threading.Tasks; // For Task

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests focused on how the VM state changes in response to processor updates,
/// highlighting changes, and busy state management.
/// </summary>
[TestClass] public class MainViewModel_StateUpdateTests : MainViewModelTestBase // Inherit from updated base
{
    // Helper to populate the LogDoc and trigger an initial filter, simulating a loaded state
    private async Task SetupWithInitialLines(IEnumerable<string> lines, IFilter? initialFilter = null)
    {
        var source = GetActiveMockSource();
        source.LinesForInitialRead.AddRange(lines);
        _mockFileDialog.FileToReturn = "C:\\test_state.log"; // Provide a dummy path

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Simulate time for async processing
        _testContext.Send(_ => { }, null); // Ensure initial load processing completes

        // Optionally apply a specific filter *after* the initial load if needed
        if (initialFilter != null && !(initialFilter is TrueFilter)) // OpenLogFile uses TrueFilter initially
        {
            Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
            _viewModel.ActiveFilterProfile.SetModelRootFilter(initialFilter);
            _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Simulate time for async processing
            _testContext.Send(_ => { }, null); // Process filter change
        }
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1], [ReqStatusBarFilteredLinesv1], [ReqSearchHighlightResultsv1] (Reset)
    [TestMethod]
    public async Task FilterChange_TriggersReplace_ClearsAndAddsLines_ResetsSearch_ClearsFilteringToken()
    {
        // Arrange: Setup with initial lines and search state
        await SetupWithInitialLines(new List<string> { "Old Line 1", "Match Me" });
        _viewModel.SearchText = "Old";
        _testContext.Send(_ => { }, null);
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count(l => l.Text.Contains("Old")), "Arrange FilteredLines count mismatch.");
        Assert.AreEqual(1, _viewModel.SearchMarkers.Count, "Arrange SearchMarkers count mismatch.");

        _viewModel.CurrentBusyStates.Clear(); // Clear initial busy state
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken); // Simulate busy
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        // Act: Change the filter (e.g., by changing context lines, which is simple)
        _viewModel.ContextLines = 1; // This triggers internal processor UpdateFilterSettings
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Simulate time for async processing
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate resulting from filter change

        // Assert: ViewModel state updated (expecting ALL lines now due to TrueFilter + context=1)
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count, "Filtered line count after context change mismatch.");
        Assert.AreEqual("Old Line 1", _viewModel.FilteredLogLines[0].Text);
        Assert.AreEqual("Match Me", _viewModel.FilteredLogLines[1].Text);
        Assert.AreEqual(2, _viewModel.FilteredLogLinesCount);
        Assert.AreEqual(0, _viewModel.SearchMarkers.Count, "Search markers should be cleared on Replace.");
        Assert.AreEqual(-1, _viewModel.CurrentMatchOffset);

        // Assert: Busy state cleared
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy state should be cleared.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken);
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod]
    public async Task FilterChange_TriggersReplace_RestoresHighlightBasedOnOriginalLineNumber()
    {
        // Arrange: Setup with specific lines
        await SetupWithInitialLines(new List<string> {
            "Line 5 Data",      // Orig 1
            "Line 10 Highlight",// Orig 2
            "Line 15 Info"      // Orig 3
        });
        // Highlight "Line 10 Highlight" (index 1 in current filtered list)
        _viewModel.HighlightedFilteredLineIndex = 1;
        _testContext.Send(_ => { }, null); // Process highlight change
        Assert.AreEqual(2, _viewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch.");

        // Arrange: Set up a new filter that will change the filtered list order/content
        // Filter for "Line" - all lines match, order maintained initially
        // Change ContextLines to trigger replace - order still maintained
        var filter = new SubstringFilter("Line");
        Assert.IsNotNull(_viewModel.ActiveFilterProfile, "ActiveFilterProfile should not be null.");
        _viewModel.ActiveFilterProfile.SetModelRootFilter(filter);
        _testContext.Send(_ => { }, null); // Process filter change
         // Highlight should still be on index 1, original line 2
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Arrange: Highlight index mismatch after filter.");
        Assert.AreEqual(2, _viewModel.HighlightedOriginalLineNumber, "Arrange: Original line number mismatch after filter.");


        // Act: Trigger a Replace update by changing ContextLines again
        _viewModel.ContextLines = 1; // Causes full refilter
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate and highlight restore

        // Assert: The filtered list content/order might change based on filter/context,
        // but the highlight should be restored to the line with OriginalLineNumber = 2.
        Assert.AreEqual(3, _viewModel.FilteredLogLines.Count, "Filtered lines count mismatch after replace.");
        // Find the new index of the line that was originally line 2
        int expectedNewIndex = _viewModel.FilteredLogLines
                                     .Select((line, index) => new { line.OriginalLineNumber, Index = index })
                                     .FirstOrDefault(item => item.OriginalLineNumber == 2)?.Index ?? -1;

        Assert.AreEqual(expectedNewIndex, _viewModel.HighlightedFilteredLineIndex, "Highlight index not restored correctly to original line 2.");
        Assert.AreEqual(2, _viewModel.HighlightedOriginalLineNumber, "Original line number incorrect after restore.");
        Assert.AreEqual(1, expectedNewIndex, "Expected new index of original line 2 is wrong."); // Double check test logic
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1] (append scenario), [ReqStatusBarFilteredLinesv1]
    [TestMethod]
    public async Task NewLineArrival_TriggersAppend_AddsOnlyNewLines_UpdatesSearch_ClearsFilteringToken()
    {
        // Arrange: Initial state
        await SetupWithInitialLines(new List<string> { "Line 1 Old", "Line 2 Old" });
        _viewModel.SearchText = "Old";
        _testContext.Send(_ => { }, null); // Run initial search
        Assert.AreEqual(2, _viewModel.SearchMarkers.Count, "Arrange SearchMarkers");

        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken); // Simulate busy
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        // Act: Simulate new lines arriving via the source mock
        var source = GetActiveMockSource();
        source.EmitLine("Line 3 New Append");
        source.EmitLine("Line 4 Old Context"); // This line contains "Old"
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Simulate time for async processing
        _testContext.Send(_ => { }, null); // Process ApplyFilteredUpdate (Append) and Search Update

        // Assert: ViewModel state updated by appending
        // Note: With TrueFilter (default from OpenLogFile), all lines appear
        Assert.AreEqual(4, _viewModel.FilteredLogLines.Count, "Append: Filtered lines count"); // 2 initial + 2 appended
        Assert.AreEqual(4, _viewModel.FilteredLogLinesCount, "Append: FilteredLogLinesCount");
        Assert.AreEqual("Line 3 New Append", _viewModel.FilteredLogLines[2].Text);
        Assert.AreEqual("Line 4 Old Context", _viewModel.FilteredLogLines[3].Text);

        // Assert: Search should now find matches in lines 1, 2, and 4.
        // UpdateSearchMatches runs within ApplyFilteredUpdate for Append now.
        Assert.AreEqual(3, _viewModel.SearchMarkers.Count, "Append: Search markers count mismatch.");

        // Assert: Busy state cleared
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Append: Busy state count");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken);
    }

    // Verifies: [ReqGeneralBusyIndicatorv1], [ReqLoadingOverlayIndicatorv1]
    public async Task BusyStates_ManagedCorrectly_DuringInitialLoad()
    {
        // Arrange
        _mockFileDialog.FileToReturn = "C:\\good\\log.txt";
        GetActiveMockSource().LinesForInitialRead = new List<string> { "Line 1" };
        _viewModel.CurrentBusyStates.Clear();
        System.Diagnostics.Debug.WriteLine($"TEST Arrange: Initial BusyStates Count = {_viewModel.CurrentBusyStates.Count}");

        // Act 1: Start the command
        System.Diagnostics.Debug.WriteLine($"TEST Act 1: Starting OpenLogFileCommand.");
        var openTask = _viewModel.OpenLogFileCommand.ExecuteAsync(null);

        // Act 2: Process ONLY the first synchronous posts from OpenLogFileAsync (adds LoadingToken)
        System.Diagnostics.Debug.WriteLine($"TEST Act 2: Calling _testContext.Send #1 (for LoadingToken).");
        _testContext.Send(_ => { }, null);
        System.Diagnostics.Debug.WriteLine($"TEST Act 2: AFTER _testContext.Send #1. BusyStates Count = {_viewModel.CurrentBusyStates.Count}");

        // Assert 1: Check for LoadingToken immediately
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "LoadingToken count mismatch early.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken missing early.");

        // Act 3: Allow Prepare to finish and FilteringToken to be posted
        System.Diagnostics.Debug.WriteLine($"TEST Act 3: Simulating Prepare completion and FilteringToken post (Delay + Send).");
        await Task.Delay(50); // Simulate time for async Prepare potentially
        _testContext.Send(_ => { }, null); // Process the post that adds FilteringToken
        System.Diagnostics.Debug.WriteLine($"TEST Act 3: AFTER _testContext.Send #2. BusyStates Count = {_viewModel.CurrentBusyStates.Count}");

        // Assert 2: Check state *during* load (BOTH tokens should be present now)
        Assert.AreEqual(2, _viewModel.CurrentBusyStates.Count, "Busy state count during load mismatch (Expected Loading+Filtering).");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken missing during load.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken missing during load.");

        // Act 4: NOW advance the background scheduler to allow filtering pipeline to run AND post back
        System.Diagnostics.Debug.WriteLine($"TEST Act 4: Advancing background scheduler for filtering.");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Act 5: Process the ApplyFilteredUpdate from the filter pipeline (removes tokens)
        System.Diagnostics.Debug.WriteLine($"TEST Act 5: Calling _testContext.Send #3 (for ApplyFilteredUpdate).");
        _testContext.Send(_ => { }, null);
        System.Diagnostics.Debug.WriteLine($"TEST Act 5: AFTER _testContext.Send #3. BusyStates Count = {_viewModel.CurrentBusyStates.Count}");

        // Act 6: Wait for the command task itself to complete (should be fast now)
        System.Diagnostics.Debug.WriteLine($"TEST Act 6: Awaiting openTask.");
        await openTask;
        System.Diagnostics.Debug.WriteLine($"TEST Act 6: openTask completed.");


        // Assert 3: Check final state *after* everything completes
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "Busy state count after load mismatch.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken not cleared after load.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken not cleared after load.");
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "Filtered lines not updated after load.");
    }

    // Verifies: [ReqGeneralBusyIndicatorv1]
    [TestMethod]
    public async Task NewLineArrival_ClearsFilteringToken_AfterAppendProcessed()
    {
         // Arrange: Setup initial state
        await SetupWithInitialLines(new List<string> { "Initial" });
        _viewModel.CurrentBusyStates.Clear();
        _viewModel.CurrentBusyStates.Add(MainViewModel.FilteringToken); // Simulate busy
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count);

        // Act: Emit a new line
        GetActiveMockSource().EmitLine("Appended Line");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Simulate time for async processing
        _testContext.Send(_ => { }, null); // Process the Append update

        // Assert
        Assert.AreEqual(0, _viewModel.CurrentBusyStates.Count, "FilteringToken not cleared after append.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken);
        Assert.AreEqual(2, _viewModel.FilteredLogLines.Count); // Verify line was added
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod]
    public async Task HighlightedFilteredLineIndex_SetValid_UpdatesOriginalLineNumber()
    {
        // Arrange: Setup initial state
        await SetupWithInitialLines(new List<string> { "Line 5", "Line 10" }); // Orig 1, 2

        // Act
        _viewModel.HighlightedFilteredLineIndex = 1; // Select "Line 10" (Orig 2)
        _testContext.Send(_ => { }, null); // Process PropertyChanged

        // Assert
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Filtered index mismatch.");
        Assert.AreEqual(2, _viewModel.HighlightedOriginalLineNumber, "Original number mismatch."); // Line 10 is the 2nd original line
    }

    // Verifies: [ReqHighlightSelectedLinev1], [ReqStatusBarSelectedLinev1]
    [TestMethod] public async Task HighlightedFilteredLineIndex_SetInvalid_ResetsOriginalLineNumber()
    {
        // Arrange: Setup initial state
        await SetupWithInitialLines(new List<string> { "Line 5" }); // Orig 1
        _viewModel.HighlightedFilteredLineIndex = 0; // Highlight "Line 5" (Orig 1)
        _testContext.Send(_ => { }, null);
        Assert.AreEqual(1, _viewModel.HighlightedOriginalLineNumber, "Arrange failure.");

        // Act: Set to -1
        _viewModel.HighlightedFilteredLineIndex = -1;
        _testContext.Send(_ => { }, null);
        // Assert
        Assert.AreEqual(-1, _viewModel.HighlightedFilteredLineIndex, "Filtered index should be -1.");
        Assert.AreEqual(-1, _viewModel.HighlightedOriginalLineNumber, "Original number should be -1.");

        // Act: Set out of bounds
        _viewModel.HighlightedFilteredLineIndex = 5; // Out of bounds (count is 1)
        _testContext.Send(_ => { }, null);
        // Assert
        Assert.AreEqual(5, _viewModel.HighlightedFilteredLineIndex, "Filtered index should be 5.");
        Assert.AreEqual(-1, _viewModel.HighlightedOriginalLineNumber, "Original number should reset to -1 on invalid index.");
    }
}
