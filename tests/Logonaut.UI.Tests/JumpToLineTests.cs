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
    // REMOVED Ignore: Test the setting of the message, delay tests clearing.
    [TestMethod] public async Task JumpToLineCommand_ValidInputLineNotFound_SetsStatusMessageAndFeedback()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "100"; // Not in FilteredLogLines
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);
        _testContext.Send(_ => { }, null); // Process any potential posts

        // Assert: Check state immediately after command execution
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        StringAssert.Contains(_viewModel.JumpStatusMessage, "not found", "Status message incorrect immediately after execution.");
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be true immediately after execution.");

        // Assert: Check state after delay (feedback should clear)
        await Task.Delay(3000); // Wait longer than the delay in TriggerInvalidInputFeedback
        _testContext.Send(_ => { }, null); // Ensure any clearing posts are processed

        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage), "Status message should be cleared after delay.");
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false after delay.");
    }

    // Verifies: [ReqGoToLineFeedbackNotFoundv1] (Invalid input)
    // REMOVED Ignore: Test the setting of the message, delay tests clearing.
    [TestMethod] public async Task JumpToLineCommand_InvalidInput_SetsStatusMessageAndFeedback()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "abc";
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);
        _testContext.Send(_ => { }, null); // Process any potential posts

        // Assert: Check state immediately after command execution
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        StringAssert.Contains(_viewModel.JumpStatusMessage, "Invalid line number", "Status message incorrect immediately after execution.");
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be true immediately after execution.");

        // Assert: Check state after delay (feedback should clear)
        await Task.Delay(3000); // Wait longer than the delay in TriggerInvalidInputFeedback
         _testContext.Send(_ => { }, null); // Ensure any clearing posts are processed

        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage), "Status message should be cleared after delay.");
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false after delay.");
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
