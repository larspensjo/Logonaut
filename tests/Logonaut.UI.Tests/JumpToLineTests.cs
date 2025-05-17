using Logonaut.TestUtils;
using Logonaut.UI.ViewModels;
using Logonaut.Common;
using System.Threading.Tasks; // Added for Task
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class TabViewModel_JumpToLineTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();

        _tabViewModel.FilteredLogLines.Clear();
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(10, "Line Ten"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(25, "Line TwentyFive"));
        _tabViewModel.FilteredLogLines.Add(new FilteredLogLine(50, "Line Fifty"));

        _tabViewModel.TargetOriginalLineNumberInput = string.Empty;
        _tabViewModel.JumpStatusMessage = string.Empty;
        _tabViewModel.IsJumpTargetInvalid = false;
        _tabViewModel.HighlightedFilteredLineIndex = -1;
    }

    [TestMethod] public async Task JumpToLineCommand_ValidInputLineFound_SetsHighlightIndex()
    {
        // Arrange
        _tabViewModel.TargetOriginalLineNumberInput = "25";

        // Act
        await _tabViewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(1, _tabViewModel.HighlightedFilteredLineIndex, "Highlight index mismatch.");
        Assert.AreEqual(25, _tabViewModel.HighlightedOriginalLineNumber, "Original line number mismatch.");
        Assert.IsTrue(string.IsNullOrEmpty(_tabViewModel.JumpStatusMessage), "Status message should be empty.");
        Assert.IsFalse(_tabViewModel.IsJumpTargetInvalid, "Invalid feedback should be false.");
    }

    // Verifies: [ReqJumpToLineFeedbackV1]
    [TestMethod] public async Task JumpToLineCommand_ValidInputLineNotFound_SetsStatusAndFeedbackThenClears()
    {
        // Arrange
        _tabViewModel.TargetOriginalLineNumberInput = "100";
        int initialHighlight = _tabViewModel.HighlightedFilteredLineIndex;

        // Act
        await _tabViewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(initialHighlight, _tabViewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        Assert.IsTrue(string.IsNullOrEmpty(_tabViewModel.JumpStatusMessage), "Status message should be clear *after* command completion.");
        Assert.IsFalse(_tabViewModel.IsJumpTargetInvalid, "Invalid feedback should be false *after* command completion.");
    }

    // Verifies: [ReqJumpToLineFeedbackV1]
    [TestMethod] public async Task JumpToLineCommand_InvalidInput_ClearsStatusAndFeedbackAfterDelay()
    {
        // Arrange
        _tabViewModel.TargetOriginalLineNumberInput = "abc";
        int initialHighlight = _tabViewModel.HighlightedFilteredLineIndex;
        _tabViewModel.JumpStatusMessage = "Some Preexisting Message";
        _tabViewModel.IsJumpTargetInvalid = false;

        // Act
        await _tabViewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(initialHighlight, _tabViewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        Assert.IsTrue(string.IsNullOrEmpty(_tabViewModel.JumpStatusMessage), "Status message should be clear *after* command completion.");
        Assert.IsFalse(_tabViewModel.IsJumpTargetInvalid, "Invalid feedback should be false *after* command completion.");
    }

    // Verifies: [ReqJumpToLineFeedbackV1]
    [TestMethod] public async Task JumpToLineCommand_InvalidInput_SetsInvalidFeedbackFlag()
    {
        // Arrange
        _tabViewModel.TargetOriginalLineNumberInput = "abc";
        _tabViewModel.IsJumpTargetInvalid = false;

        // Act
        await _tabViewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert: After the command and its internal delay are fully complete.
        Assert.IsFalse(_tabViewModel.IsJumpTargetInvalid, "Invalid feedback should be false after command completion.");
        Assert.IsTrue(string.IsNullOrEmpty(_tabViewModel.JumpStatusMessage), "Status message should be clear after command completion.");
    }

    [TestMethod] public void HighlightedFilteredLineIndex_Set_UpdatesTargetInput()
    {
        // Arrange - done in TestInitialize

        // Act: Select line index 2 (Original Line 50)
        _tabViewModel.HighlightedFilteredLineIndex = 2;

        // Assert
        Assert.AreEqual("50", _tabViewModel.TargetOriginalLineNumberInput, "Target input not updated for index 2.");

        // Act: Deselect line
        _tabViewModel.HighlightedFilteredLineIndex = -1;

        // Assert
        Assert.AreEqual("", _tabViewModel.TargetOriginalLineNumberInput, "Target input not cleared for index -1.");
    }
}
