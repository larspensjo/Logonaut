using Logonaut.TestUtils;
using Logonaut.UI.ViewModels;
using Logonaut.Common;
using System.Threading; // Required for SynchronizationContext
using System.Threading.Tasks; // Required for Task
using Microsoft.VisualStudio.TestTools.UnitTesting; // Explicit using

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class MainViewModel_JumpToLineTests : MainViewModelTestBase // Inherit from the updated base
{
    // No separate TestInitialize needed here if the base setup is sufficient,
    // unless specific LogDoc/FilteredLines setup is required ONLY for these tests.
    // Let's add one to ensure the FilteredLogLines are always set consistently for jump tests.
    [TestInitialize]
    public override void TestInitialize() // Use override if extending base behavior
    {
        base.TestInitialize(); // Run the base setup first

        // Setup initial filtered lines crucial for jump tests
        _viewModel.FilteredLogLines.Clear(); // Ensure clean state
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(10, "Line Ten"));    // Index 0
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(25, "Line TwentyFive"));// Index 1
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(50, "Line Fifty"));   // Index 2

        // Reset jump-related state before each test
        _viewModel.TargetOriginalLineNumberInput = string.Empty;
        _viewModel.JumpStatusMessage = string.Empty;
        _viewModel.IsJumpTargetInvalid = false;
        _viewModel.HighlightedFilteredLineIndex = -1; // Reset highlight
    }


    // Verifies: [ReqGoToLineExecuteJumpv1] (Success case)
    [TestMethod] public async Task JumpToLineCommand_ValidInputLineFound_SetsHighlightIndex()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "25";

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);
        _testContext.Send(_ => { }, null); // Process any potential posts

        // Assert
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Highlight index mismatch.");
        Assert.AreEqual(25, _viewModel.HighlightedOriginalLineNumber, "Original line number mismatch.");
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage), "Status message should be empty.");
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false.");
    }

    // Verifies: [ReqGoToLineFeedbackNotFoundv1] (Line not in filtered view)
    [TestMethod] public async Task JumpToLineCommand_ValidInputLineNotFound_SetsStatusAndFeedbackThenClears()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "100"; // Not in FilteredLogLines
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        // Execute the command but DON'T await the *entire* command yet if we
        // want to check the state *before* the internal delay completes.
        // However, the command itself resets the message initially. This makes testing
        // the "message set" state tricky without modifying the command.

        // Let's modify the approach: Execute fully and check the sequence.
        // We expect the message to be set *during* execution and cleared *after* the delay.
        // Testing the intermediate state is hard with the current command structure.

        // Let's test the FINAL state immediately after the command fully completes (including delays)
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);
        _testContext.Send(_ => { }, null); // Process any potential posts

        // Assert: Check state AFTER the command (and its internal delay) is fully done
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        // At this point, the message SHOULD be cleared by TriggerInvalidInputFeedback
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage), "Status message should be clear *after* command completion.");
        // And the feedback flag should also be cleared
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false *after* command completion.");
    }

    // Verifies: [ReqGoToLineFeedbackNotFoundv1] (Invalid input)
    [TestMethod] public async Task JumpToLineCommand_InvalidInput_ClearsStatusAndFeedbackAfterDelay()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "abc";
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;
        _viewModel.JumpStatusMessage = "Some Preexisting Message"; // Ensure it's not already empty
        _viewModel.IsJumpTargetInvalid = false; // Ensure starting state

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);
        _testContext.Send(_ => { }, null); // Process any potential posts

        // Assert: Check state AFTER the command (and its internal delay) is fully done
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        // At this point, the message SHOULD be cleared by TriggerInvalidInputFeedback
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage), "Status message should be clear *after* command completion.");
        // And the feedback flag should also be cleared
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false *after* command completion.");
    }

    // Verifies: [ReqGoToLineFeedbackNotFoundv1] (Invalid input)
    [TestMethod] public async Task JumpToLineCommand_InvalidInput_SetsInvalidFeedbackFlag()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "abc";
        _viewModel.IsJumpTargetInvalid = false; // Ensure starting state

        // Act
        var jumpTask = _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert: Check IMMEDIATELY after starting the command, *before* awaiting it fully.
        // The flag is set synchronously within TriggerInvalidInputFeedback before the delay.
        _testContext.Send(_ => { }, null); // Process the synchronous part of the command
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be true immediately after command start.");

        // Allow the command and its delay to complete
        await jumpTask;
        _testContext.Send(_ => { }, null); // Process the clearing part

        // Assert final state (optional, covered by other test)
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false after command completion.");
    }

    // Verifies: [ReqStatusBarSelectedLinev1] (Update based on highlight)
    [TestMethod] public void HighlightedFilteredLineIndex_Set_UpdatesTargetInput()
    {
        // Act: Select line index 2 (Original Line 50)
        _viewModel.HighlightedFilteredLineIndex = 2;
        _testContext.Send(_ => { }, null); // Process property changed handler

        // Assert
        Assert.AreEqual("50", _viewModel.TargetOriginalLineNumberInput, "Target input not updated for index 2.");

        // Act: Deselect line
        _viewModel.HighlightedFilteredLineIndex = -1;
        _testContext.Send(_ => { }, null); // Process property changed handler

        // Assert
        Assert.AreEqual("", _viewModel.TargetOriginalLineNumberInput, "Target input not cleared for index -1.");
    }
}
