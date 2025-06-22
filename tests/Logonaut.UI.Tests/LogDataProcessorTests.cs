using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.UI.ViewModels; // For LogDataProcessor, SourceType
using Logonaut.Core;         // For ILogSource, FilteredUpdateBase, etc.
using Logonaut.Common;       // For FilteredLogLine, LogDocument
using Logonaut.Filters;      // For TrueFilter, SubstringFilter
using Logonaut.TestUtils;    // For MockLogSource, MockSettingsService etc. from base
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Reactive.Testing; // For TestScheduler and ReactiveAssert (if needed)
using System.IO; // For IOException in tests

namespace Logonaut.UI.Tests.ViewModels;

/**
 * Unit tests for the <LogDataProcessor class, focusing on its core
 * responsibilities of activating log sources, processing log data, applying filters,
 * and managing its lifecycle.
 * These tests utilize mocks for external dependencies like ILogSourceProvider and
 * leverage TestScheduler for controlling time-based reactive operations.
 */
[TestClass] public class LogDataProcessorTests : MainViewModelTestBase
{
    private LogDataProcessor _processor = null!;
    private List<FilteredUpdateBase> _receivedFilteredUpdates = null!;
    private List<long> _receivedTotalLines = null!;
    private IDisposable _filteredSubscription = null!;
    private IDisposable _totalLinesSubscription = null!;

    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();

        _receivedFilteredUpdates = new List<FilteredUpdateBase>();
        _receivedTotalLines = new List<long>();

        _processor = new LogDataProcessor(
            _mockSourceProvider,
            _testContext,
            _backgroundScheduler
        );

        _filteredSubscription = _processor.FilteredLogUpdates.Subscribe(update => {
            System.Diagnostics.Debug.WriteLine($"_filteredSubscription TEST RECEIVED FilteredUpdate: Type={update.GetType().Name}, IsInitialLoadProcessingComplete (if Replace)={(update as ReplaceFilteredUpdate)?.IsInitialLoadProcessingComplete}");
            _receivedFilteredUpdates.Add(update);
        });
        _totalLinesSubscription = _processor.TotalLinesProcessed.Subscribe(count => _receivedTotalLines.Add(count));
        _mockSourceProvider.Clear();
    }

    [TestCleanup] public override void TestCleanup()
    {
        // Act
        _filteredSubscription?.Dispose();
        _totalLinesSubscription?.Dispose();
        _processor?.Dispose();
        base.TestCleanup();
    }

    // Verifies: [ReqFileMonitorLiveUpdatev1]
    [TestMethod] public async Task ActivateAsync_WithFileSource_LoadsInitialLinesAndFilters()
    {
        // Arrange
        var initialLines = new List<string> { "line1", "INFO: line2", "line3" };
        _mockFileLogSource.LinesForInitialRead = initialLines;
        var initialFilter = new SubstringFilter("INFO");
        int initialContextLines = 0;
        string filePath = "C:\\test.log";

        // Act
        await _processor.ActivateAsync(SourceType.File, filePath, initialFilter, initialContextLines, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        // Assert
        Assert.AreEqual(1, _mockSourceProvider.GetCreateCount(), "CreateFileLogSource should be called once.");
        Assert.AreEqual(filePath, _mockFileLogSource.PreparedSourceIdentifier, "PrepareAndGetInitialLinesAsync called with correct path.");
        Assert.AreEqual(initialLines.Count, _processor.LogDocDeprecated.Count, "LogDocument should contain initial lines.");
        Assert.IsTrue(_mockFileLogSource.IsRunning, "StartMonitoring (Start) should be called on the log source.");
        Assert.IsTrue(_receivedTotalLines.Any(), "TotalLinesProcessed should have emitted values.");
        Assert.AreEqual(initialLines.Count, _receivedTotalLines.LastOrDefault(), "TotalLinesProcessed should emit initial line count.");
        Assert.IsTrue(_receivedFilteredUpdates.Any(), "FilteredLogUpdates should have emitted values.");
        var lastUpdate = _receivedFilteredUpdates.LastOrDefault() as ReplaceFilteredUpdate;
        Assert.IsNotNull(lastUpdate, "Last update should be ReplaceFilteredUpdate for initial load.");
        Assert.IsTrue(lastUpdate.IsInitialLoadProcessingComplete, "ReplaceFilteredUpdate should mark initial load as complete.");
        Assert.AreEqual(1, lastUpdate.Lines.Count, "Should have 1 filtered line based on 'INFO' filter.");
        Assert.AreEqual("INFO: line2", lastUpdate.Lines[0].Text);
        Assert.AreEqual(2, lastUpdate.Lines[0].OriginalLineNumber);
    }

    // Verifies: [ReqDisplayRealTimeUpdatev1]
    [TestMethod] public async Task NewLines_FromFileSource_AreAppendedAndFiltered()
    {
        // Arrange
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Initial INFO line" };
        var filter = new SubstringFilter("INFO");
        await _processor.ActivateAsync(SourceType.File, "C:\\test.log", filter, 0, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
        _receivedFilteredUpdates.Clear();

        // Act
        _mockFileLogSource.EmitLine("New DEBUG line");
        _mockFileLogSource.EmitLine("Another New INFO line");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        // Assert
        Assert.AreEqual(1, _receivedFilteredUpdates.Count, "Should receive one AppendFilteredUpdate for the batch.");
        var appendUpdate = _receivedFilteredUpdates.FirstOrDefault() as AppendFilteredUpdate;
        Assert.IsNotNull(appendUpdate, "Update should be AppendFilteredUpdate.");
        Assert.AreEqual(1, appendUpdate.Lines.Count, "Append update should contain one matching line.");
        Assert.AreEqual("Another New INFO line", appendUpdate.Lines[0].Text);
        Assert.AreEqual(3, appendUpdate.Lines[0].OriginalLineNumber);
        Assert.AreEqual(3, _processor.LogDocDeprecated.Count, "LogDocument should contain all 3 lines.");
        Assert.AreEqual(3, _receivedTotalLines.LastOrDefault(), "TotalLinesProcessed should be 3.");
    }

    // Verifies: [ReqFilterDynamicUpdateViewv1]
    [TestMethod] public async Task ApplyFilterSettings_TriggersReFilter()
    {
        // Arrange
        var initialLines = new List<string> { "Error line", "Info line", "Debug line" };
        _mockFileLogSource.LinesForInitialRead = initialLines;
        await _processor.ActivateAsync(SourceType.File, "C:\\test.log", new SubstringFilter("Error"), 0, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
        var initialFilteredUpdate = _receivedFilteredUpdates.LastOrDefault() as ReplaceFilteredUpdate;
        Assert.IsNotNull(initialFilteredUpdate, "Initial update was null");
        Assert.AreEqual(1, initialFilteredUpdate.Lines.Count, "Initial filter 'Error' should yield 1 line.");
        _receivedFilteredUpdates.Clear();

        // Act
        var newFilter = new SubstringFilter("Info");
        _processor.ApplyFilterSettings(newFilter, 0);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        // Assert
        Assert.IsTrue(_receivedFilteredUpdates.Any(), "Should receive an update after filter change.");
        var replaceUpdate = _receivedFilteredUpdates.LastOrDefault() as ReplaceFilteredUpdate;
        Assert.IsNotNull(replaceUpdate, "Update should be ReplaceFilteredUpdate after filter change.");
        Assert.IsFalse(replaceUpdate.IsInitialLoadProcessingComplete, "IsInitialLoadProcessingComplete should be false for subsequent re-filters.");
        Assert.AreEqual(1, replaceUpdate.Lines.Count, "Should have 1 line matching 'Info'.");
        Assert.AreEqual("Info line", replaceUpdate.Lines[0].Text);
        Assert.AreEqual(2, replaceUpdate.Lines[0].OriginalLineNumber);
    }

    // Verifies: [ReqPasteFromClipboardv1]
    [TestMethod] public async Task LoadPastedLogContent_PopulatesDocument_AndActivateFilters()
    {
        // Arrange
        string pastedText = "Pasted line 1\r\nPasted INFO line 2";
        var filter = new SubstringFilter("INFO");

        // Act
        _processor.LoadPastedLogContent(pastedText);
        Assert.AreEqual(2, _processor.LogDocDeprecated.Count, "LogDocument not populated correctly by LoadPastedLogContent.");
        Assert.AreEqual(2, _receivedTotalLines.LastOrDefault(), "TotalLinesProcessed not updated after LoadPastedLogContent.");
        await _processor.ActivateAsync(SourceType.Pasted, null, filter, 0, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        // Assert
        Assert.IsTrue(_receivedFilteredUpdates.Any(), "FilteredLogUpdates should have emitted after activating pasted content.");
        var replaceUpdate = _receivedFilteredUpdates.LastOrDefault() as ReplaceFilteredUpdate;
        Assert.IsNotNull(replaceUpdate, "Update should be ReplaceFilteredUpdate.");
        Assert.IsTrue(replaceUpdate.IsInitialLoadProcessingComplete, "Pasted content processing should mark initial load as complete.");
        Assert.AreEqual(1, replaceUpdate.Lines.Count, "Should filter pasted content.");
        Assert.AreEqual("Pasted INFO line 2", replaceUpdate.Lines[0].Text);
    }

    [TestMethod] public async Task Deactivate_StopsSourceAndStream()
    {
        // Arrange
        await _processor.ActivateAsync(SourceType.File, "C:\\test.log", new TrueFilter(), 0, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        Assert.IsTrue(_mockFileLogSource.IsRunning, "Source should be monitoring before deactivate.");
        Assert.IsNotNull(_processor.ReactiveFilteredLogStream, "Reactive stream should exist before deactivate.");

        // Act
        _processor.Deactivate(clearLogDocument: true);

        // Assert
        Assert.IsFalse(_mockFileLogSource.IsRunning, "Source monitoring should be stopped.");
        Assert.IsNull(_processor.ReactiveFilteredLogStream, "ReactiveFilteredLogStream should be null after deactivate.");
        Assert.IsNull(_processor.LogSource, "LogSource should be null after deactivate.");
        Assert.AreEqual(0, _processor.LogDocDeprecated.Count, "LogDocument should be cleared if clearLogDocument is true.");
        Assert.AreEqual(0, _receivedTotalLines.LastOrDefault(), "TotalLinesProcessed should be reset if document cleared.");
    }

    // Verifies: [ReqFilterContextLinesv1]
    [TestMethod] public async Task NewLines_WithContext_AreAppendedAndFiltered()
    {
        // Arrange
        _mockFileLogSource.LinesForInitialRead = new List<string> { "context before", "MATCH_ME", "context after" };
        var filter = new SubstringFilter("MATCH_ME");
        int contextLines = 1;
        await _processor.ActivateAsync(SourceType.File, "C:\\test.log", filter, contextLines, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);
        var initialUpdate = _receivedFilteredUpdates.LastOrDefault() as ReplaceFilteredUpdate;
        Assert.IsNotNull(initialUpdate, "Initial update was null.");
        Assert.AreEqual(3, initialUpdate.Lines.Count, "Initial load should include context lines.");
        Assert.IsTrue(initialUpdate.Lines.Any(l => l.Text == "context before" && l.IsContextLine));
        Assert.IsTrue(initialUpdate.Lines.Any(l => l.Text == "MATCH_ME" && !l.IsContextLine));
        Assert.IsTrue(initialUpdate.Lines.Any(l => l.Text == "context after" && l.IsContextLine));
        _receivedFilteredUpdates.Clear();

        // Act
        _mockFileLogSource.EmitLine("another context before");
        _mockFileLogSource.EmitLine("NEW_MATCH_ME");
        _mockFileLogSource.EmitLine("another context after");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        // Assert
        var appendUpdate = _receivedFilteredUpdates.LastOrDefault() as AppendFilteredUpdate;
        Assert.IsNotNull(appendUpdate, "Append update should have occurred.");
        Assert.AreEqual(3, appendUpdate.Lines.Count, "Append update should contain new match and its context.");
        var appendedLines = appendUpdate.Lines.OrderBy(l => l.OriginalLineNumber).ToList();
        Assert.AreEqual("another context before", appendedLines[0].Text);
        Assert.IsTrue(appendedLines[0].IsContextLine);
        Assert.AreEqual(4, appendedLines[0].OriginalLineNumber);
        Assert.AreEqual("NEW_MATCH_ME", appendedLines[1].Text);
        Assert.IsFalse(appendedLines[1].IsContextLine);
        Assert.AreEqual(5, appendedLines[1].OriginalLineNumber);
        Assert.AreEqual("another context after", appendedLines[2].Text);
        Assert.IsTrue(appendedLines[2].IsContextLine);
        Assert.AreEqual(6, appendedLines[2].OriginalLineNumber);
        Assert.AreEqual(6, _processor.LogDocDeprecated.Count);
        Assert.AreEqual(6, _receivedTotalLines.LastOrDefault());
    }

    // Verifies: [ReqLogSimulatorv1]
    [TestMethod] public async Task ActivateAsync_WithSimulatorSource_StartsSimulator()
    {
        // Arrange
        _mockSimulatorSource.LinesForInitialRead = new List<string>();
        var initialFilter = new TrueFilter();
        string simulatorId = "TestSim";

        // Act
        await _processor.ActivateAsync(SourceType.Simulator, simulatorId, initialFilter, 0, null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(300).Ticks);

        // Assert
        Assert.AreEqual(1, _mockSourceProvider.GetCreateCount(), "CreateSimulatorLogSource should be called once.");
        Assert.AreEqual(simulatorId, _mockSimulatorSource.PreparedSourceIdentifier, "PrepareAndGetInitialLinesAsync called with correct ID for simulator.");
        Assert.IsTrue(_mockSimulatorSource.IsRunning, "StartMonitoring (Start) should be called on the simulator source.");
        Assert.AreEqual(0, _processor.LogDocDeprecated.Count, "LogDocument should be empty for new simulator.");
        Assert.AreEqual(0, _receivedTotalLines.LastOrDefault(), "TotalLinesProcessed should be 0 for new simulator.");
        var lastUpdate = _receivedFilteredUpdates.LastOrDefault() as ReplaceFilteredUpdate;
        Assert.IsNotNull(lastUpdate, "An initial ReplaceFilteredUpdate should occur.");
        Assert.IsTrue(lastUpdate.IsInitialLoadProcessingComplete, "Initial load for simulator should be marked complete.");
        Assert.AreEqual(0, lastUpdate.Lines.Count, "Filtered lines should be empty for new simulator.");
    }

    [TestMethod] public async Task ActivateAsync_FileSourcePrepareFails_ThrowsAndCleansUp()
    {
        // Arrange
        string failingFilePath = "FAIL_PREPARE";
        var initialFilter = new TrueFilter();
        Exception? caughtException = null;

        // Act
        try
        {
            await _processor.ActivateAsync(SourceType.File, failingFilePath, initialFilter, 0, null);
        }
        catch (IOException ex)
        {
            caughtException = ex;
        }
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);

        // Assert
        Assert.IsNotNull(caughtException, "ActivateAsync should throw if PrepareAndGetInitialLinesAsync fails.");
        Assert.IsTrue(caughtException.Message.Contains("Mock Prepare Failed"), "Exception message should match the mock's failure.");
        Assert.AreEqual(1, _mockSourceProvider.GetCreateCount(), "CreateFileLogSource should still be called.");
        Assert.AreEqual(failingFilePath, _mockFileLogSource.PreparedSourceIdentifier, "PrepareAndGetInitialLinesAsync was called.");
        Assert.IsFalse(_mockFileLogSource.IsRunning, "StartMonitoring should not be called if prepare fails.");
        Assert.IsNull(_processor.LogSource, "LogSource should be null after failed activation.");
        Assert.IsNull(_processor.ReactiveFilteredLogStream, "ReactiveFilteredLogStream should be null after failed activation.");
        Assert.AreEqual(0, _processor.LogDocDeprecated.Count, "LogDocument should be empty or cleared on failure.");
    }
}
