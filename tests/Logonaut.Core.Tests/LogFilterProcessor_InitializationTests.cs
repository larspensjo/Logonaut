using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // For Task
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.TestUtils; // For mocks

namespace Logonaut.Core.Tests; // File-scoped namespace

/// <summary>
/// Tests focused on the initialization, reset, and initial filtering behavior
/// of the LogFilterProcessor.
/// </summary>
[TestClass] public class LogFilterProcessor_InitializationTests : LogFilterProcessorTestBase
{
    // Verifies: Internal processor state initialization
    [TestMethod] public void Constructor_InitializesStreamsCorrectly()
    {
        // Arrange (Processor created in base TestInitialize)

        // Assert
        Assert.IsNotNull(_processor.FilteredUpdates, "FilteredUpdates observable should not be null.");
        Assert.IsNotNull(_processor.TotalLinesProcessed, "TotalLinesProcessed observable should not be null.");
        // Check initial state of BehaviorSubjects if necessary (though tested indirectly elsewhere)
        long initialTotal = 0;
        var totalSub = _processor.TotalLinesProcessed.Subscribe(l => initialTotal = l); // Subscribe to get current value
        Assert.AreEqual(0, initialTotal, "Initial TotalLinesProcessed should be 0.");
        totalSub.Dispose();
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Initial filter path), [ReqLargeFileResponsivev1] (Background processing initiation)
    [TestMethod] public async Task FirstFilter_RunsAfterPrepareAndStart_And_UpdateFilterSettings()
    {
        // Arrange
        var initialLines = new List<string> { "Initial 1", "Initial 2" };
        _receivedUpdates.Clear(); // Clear any updates from base initialize

        // Act: Use the async setup helper which includes the sequence
        await SetupInitialFileLoad(initialLines, new TrueFilter(), 0);

        // Assert: LogDocument state verified inside helper.
        // Check no *further* updates were received after setup cleared the list.
        Assert.AreEqual(0, _receivedUpdates.Count, "No further updates expected immediately after setup completed and cleared updates.");

        // Assert initial filter was processed during setup
        // (SetupInitialFileLoad advances scheduler, allowing the initial filter to run)
        // This check is implicitly covered by SetupInitialFileLoad clearing updates after the initial one runs.
    }

    // Verifies: [ReqFileMonitorLiveUpdatev1] (Reset for new file), [ReqPasteFromClipboardv1] (Reset for paste)
    [TestMethod] public async Task Reset_ClearsProcessorState_LogDocumentPopulatedByPrepare()
    {
        // Arrange: Perform an initial load first
        await SetupInitialFileLoad(new List<string> { "Old Line 1" });
        Assert.AreEqual(1, _logDocument.Count); // Verify initial state
        long initialTotalLines = 0;
        var totalSub = _processor.TotalLinesProcessed.Subscribe(l => initialTotalLines = l);
        Assert.AreEqual(1, initialTotalLines, "Total lines should be 1 after first load.");
        totalSub.Dispose();

        // Act: Reset
        _processor.Reset();

        // Assert: Processor index reset, LogDocument *NOT* cleared by processor anymore
        Assert.AreEqual(1, _logDocument.Count, "LogDocument should NOT be cleared by processor Reset.");
        // Assert total lines observable reset
        totalSub = _processor.TotalLinesProcessed.Subscribe(l => initialTotalLines = l);
        Assert.AreEqual(0, initialTotalLines, "Total lines should reset to 0 after Reset().");
        totalSub.Dispose();

        // Act: Load a new file using the setup helper (which calls Prepare again)
        var newLines = new List<string> { "New Line A" };
        await SetupInitialFileLoad(newLines); // Setup clears LogDoc before calling Prepare

        // Assert: Document contains only the new lines after setup
        Assert.AreEqual(1, _logDocument.Count);
        Assert.AreEqual("New Line A", _logDocument[0]);
        // Assert total lines updated by new load's ApplyFullFilter
        totalSub = _processor.TotalLinesProcessed.Subscribe(l => initialTotalLines = l);
        Assert.AreEqual(1, initialTotalLines, "Total lines should be 1 after second load.");
        totalSub.Dispose();
    }
}
