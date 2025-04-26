// ===== File: tests\Logonaut.UI.Tests\JumpToLineTests.cs =====

using Logonaut.TestUtils;
using Logonaut.UI.ViewModels;
using Logonaut.Common;
using System.Threading; // Required for SynchronizationContext
using System.Threading.Tasks; // Required for Task

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class MainViewModel_JumpToLineTests
{
    private MockSettingsService _mockSettings = null!;
    private SynchronizationContext _testContext = null!;
    private MainViewModel _viewModel = null!;

    [TestInitialize] public void TestInitialize()
    {
        // Use mocks from Logonaut.TestUtils
        _mockSettings = new MockSettingsService();
        _testContext = new ImmediateSynchronizationContext(); // From TestUtils

        // --- MODIFIED: Instantiate MainViewModel without tailer service ---
        // The VM will create its own internal ILogSource (FileLogSource by default)
        _viewModel = new MainViewModel(
            _mockSettings,
            // fileDialogService: null, // Can omit if not needed for these tests
            // logFilterProcessor: null, // Can omit if not needed
            // initialLogSource: null, // Can omit
            uiContext: _testContext
        );

        // Setup some initial filtered lines for testing
        // This state is crucial for the jump tests
        _viewModel.FilteredLogLines.Clear(); // Ensure clean state
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(10, "Line Ten"));    // Index 0
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(25, "Line TwentyFive"));// Index 1
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(50, "Line Fifty"));   // Index 2
    }

    // --- Test methods remain the same as they test UI state logic ---

    [TestMethod] public async Task JumpToLineCommand_ValidInputLineFound_SetsHighlightIndex()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "25";

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(1, _viewModel.HighlightedFilteredLineIndex);
        Assert.AreEqual(25, _viewModel.HighlightedOriginalLineNumber);
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.JumpStatusMessage));
        Assert.IsFalse(_viewModel.IsJumpTargetInvalid);
    }

    [TestMethod]
    [Ignore("Can't test the message as it is cleared before JumpToLineCommand returns")]
    public async Task JumpToLineCommand_ValidInputLineNotFound_SetsStatusMessage()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "100"; // Not in FilteredLogLines
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex); // Highlight shouldn't change
        StringAssert.Contains(_viewModel.JumpStatusMessage, "not found");
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid); // Feedback triggered
    }

    [TestMethod]
    [Ignore("Can't test the message as it is cleared before JumpToLineCommand returns")]
    public async Task JumpToLineCommand_InvalidInput_SetsStatusMessage()
    {
        // Arrange
        _viewModel.TargetOriginalLineNumberInput = "abc";
        int initialHighlight = _viewModel.HighlightedFilteredLineIndex;

        // Act
        await _viewModel.JumpToLineCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(initialHighlight, _viewModel.HighlightedFilteredLineIndex);
        StringAssert.Contains(_viewModel.JumpStatusMessage, "Invalid line number");
        Assert.IsTrue(_viewModel.IsJumpTargetInvalid);
    }

    [TestMethod] public void HighlightedFilteredLineIndex_Set_UpdatesTargetInput()
    {
        // Act
        _viewModel.HighlightedFilteredLineIndex = 2; // Corresponds to Original Line 50

        // Assert
        Assert.AreEqual("50", _viewModel.TargetOriginalLineNumberInput);

        // Act
        _viewModel.HighlightedFilteredLineIndex = -1;

        // Assert
        Assert.AreEqual("", _viewModel.TargetOriginalLineNumberInput);
    }
}
