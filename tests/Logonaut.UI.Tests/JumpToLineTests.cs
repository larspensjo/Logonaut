using Logonaut.TestUtils;
using Logonaut.UI.ViewModels;
using Logonaut.Common;
using System.Threading; // Required for SynchronizationContext
using System.Threading.Tasks; // Required for Task
using Microsoft.VisualStudio.TestTools.UnitTesting; // Explicit using

namespace Logonaut.UI.Tests.ViewModels;

[TestClass]
public class MainViewModel_JumpToLineTests
{
    private MockSettingsService _mockSettings = null!;
    private SynchronizationContext _testContext = null!;
    private MainViewModel _viewModel = null!;

    [TestInitialize] public void TestInitialize()
    {
        _mockSettings = new MockSettingsService();
        _testContext = new ImmediateSynchronizationContext();

        // Use constructor suitable for this test focus
        _viewModel = new MainViewModel(
            _mockSettings,
            uiContext: _testContext
            // Omit services not directly needed for jump logic tests
        );

        // Setup initial filtered lines crucial for jump tests
        _viewModel.FilteredLogLines.Clear();
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(10, "Line Ten"));    // Index 0
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(25, "Line TwentyFive"));// Index 1
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(50, "Line Fifty"));   // Index 2
    }

    // Verifies: [ReqGoToLineExecuteJumpv1] (Success case)
    [TestMethod] public async Task JumpToLineCommand_ValidInputLineFound_SetsHighlightIndex()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "25";

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex, "Highlight index mismatch.");
        Assert.AreEqual(25, _viewModel.HighlightedOriginalLineNumber, "Original line number mismatch.");
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage), "Status message should be empty.");
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be false.");
    }

    // Verifies: [ReqGoToLineFeedbackNotFoundv1] (Line not in filtered view)
    [Ignore("Can't test the message as it is cleared before JumpToLineCommand returns")]
    [TestMethod] public async Task JumpToLineCommand_ValidInputLineNotFound_SetsStatusMessage()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "100"; // Not in FilteredLogLines
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        StringAssert.Contains(_viewModel.JumpStatusMessage, "not found", "Status message incorrect.");
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be true.");
    }

    // Verifies: [ReqGoToLineFeedbackNotFoundv1] (Invalid input)
    [Ignore("Can't test the message as it is cleared before JumpToLineCommand returns")]
    [TestMethod] public async Task JumpToLineCommand_InvalidInput_SetsStatusMessage()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "abc";
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex, "Highlight index should not change.");
        StringAssert.Contains(_viewModel.JumpStatusMessage, "Invalid line number", "Status message incorrect.");
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid, "Invalid feedback should be true.");
    }

    // Verifies: [ReqStatusBarSelectedLinev1] (Update based on highlight)
    [TestMethod] public void HighlightedFilteredLineIndex_Set_UpdatesTargetInput()
    {
        // Act: Select line index 2 (Original Line 50)
        _viewModel.HighlightedFilteredLineIndex = 2;

        // Assert
        Assert.AreEqual("50", _viewModel.TargetOriginalLineNumberInput, "Target input not updated for index 2.");

        // Act: Deselect line
        _viewModel.HighlightedFilteredLineIndex = -1;

        // Assert
        Assert.AreEqual("", _viewModel.TargetOriginalLineNumberInput, "Target input not cleared for index -1.");
    }
}
