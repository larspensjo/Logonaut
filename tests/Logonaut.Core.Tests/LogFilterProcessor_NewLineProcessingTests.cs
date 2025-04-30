using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks; // For Task
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.TestUtils; // For mocks
using Microsoft.Reactive.Testing; // For TestScheduler specifics

namespace Logonaut.Core.Tests; // File-scoped namespace

/// <summary>
/// Tests focused on how the LogFilterProcessor handles new lines arriving
/// from the log source, including throttling and filtering logic.
/// </summary>
[TestClass] public class LogFilterProcessor_NewLineProcessingTests : LogFilterProcessorTestBase
{
    // Verifies: [ReqFilterEfficientRealTimev1] (Current throttling behavior)
    [TestMethod] public async Task NewLines_TriggerSingleThrottledReplaceUpdate_WithAllLines()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Initial A", "Initial B" };
        await SetupInitialFileLoad(initialLines, new TrueFilter(), 0); // Clears _receivedUpdates

        // Act: Emit lines *after* setup (monitoring should be active)
        _mockLogSource.EmitLine("Line C");
        _mockLogSource.EmitLine("Line D");

        // Assert: No updates yet because of throttle
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Advance scheduler *past* throttle time
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: LogDocument contains all lines (initial + emitted)
        Assert.AreEqual(4, _logDocument.Count);
        Assert.AreEqual("Initial A", _logDocument[0]);
        Assert.AreEqual("Initial B", _logDocument[1]);
        Assert.AreEqual("Line C", _logDocument[2]);
        Assert.AreEqual("Line D", _logDocument[3]);

        // Assert: ONE Replace update received containing ALL lines
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(4, update.Lines.Count);
        CollectionAssert.AreEqual(new List<string> { "Initial A", "Initial B", "Line C", "Line D" }, GetLinesText(update));
        Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
        Assert.AreEqual(4, update.Lines[3].OriginalLineNumber);
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Filtering applied), [ReqFilterDisplayMatchingLinesv1]
    [TestMethod] public async Task NewLines_TriggerFilteredReplaceUpdate_OnEntireDocument()
    {
        // Arrange: Setup initial load
        var initialLines = new List<string> { "Initial INFO" };
        await SetupInitialFileLoad(initialLines); // Uses TrueFilter initially, clears updates

        // Arrange: Set specific filter *after* initial load
        var filter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(filter, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for filter change
        Assert.AreEqual(1, _receivedUpdates.Count, "Setting MATCH filter should trigger Replace update.");
        Assert.AreEqual(0, _receivedUpdates.Last().Lines.Count, "Filter change update should be empty.");
        _receivedUpdates.Clear(); // Clear the filter update

        // Act: Emit new lines
        _mockLogSource.EmitLine("IGNORE 1");
        _mockLogSource.EmitLine("MATCH 2");
        _mockLogSource.EmitLine("IGNORE 3");
        _mockLogSource.EmitLine("MATCH 4");

        // Advance scheduler *past* throttle time for the trigger caused by new lines
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: LogDocument contains all lines
        Assert.AreEqual(5, _logDocument.Count);
        Assert.AreEqual("Initial INFO", _logDocument[0]);
        Assert.AreEqual("IGNORE 1", _logDocument[1]);
        Assert.AreEqual("MATCH 2", _logDocument[2]);
        Assert.AreEqual("IGNORE 3", _logDocument[3]);
        Assert.AreEqual("MATCH 4", _logDocument[4]);

        // Assert: Received ONE Replace update filtered on the *entire* LogDocument
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(2, update.Lines.Count);
        Assert.AreEqual("MATCH 2", update.Lines[0].Text);
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual("MATCH 4", update.Lines[1].Text);
        Assert.AreEqual(5, update.Lines[1].OriginalLineNumber);
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqDisplayOriginalLineNumbersv1]
    [TestMethod] public async Task NewLines_TriggerReplaceUpdate_UsesCorrectOriginalLineNumbers()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Line 1", "Line 2", "Line 3" };
        await SetupInitialFileLoad(initialLines, new TrueFilter(), 0); // Clears updates

        // Act: Emit new lines, some before throttle, some after
        _mockLogSource.EmitLine("Line A");
        _mockLogSource.EmitLine("Line B");
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        _mockLogSource.EmitLine("Line C");
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Past throttle window

        // Assert: LogDocument contains all lines
        Assert.AreEqual(6, _logDocument.Count);
        Assert.AreEqual("Line 1", _logDocument[0]);
        Assert.AreEqual("Line 2", _logDocument[1]);
        Assert.AreEqual("Line 3", _logDocument[2]);
        Assert.AreEqual("Line A", _logDocument[3]);
        Assert.AreEqual("Line B", _logDocument[4]);
        Assert.AreEqual("Line C", _logDocument[5]);

        // Assert: Received ONE Replace update with ALL lines and correct OriginalLineNumbers
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(6, update.Lines.Count);
        CollectionAssert.AreEqual(new List<string> { "Line 1", "Line 2", "Line 3", "Line A", "Line B", "Line C" }, GetLinesText(update));
        Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
        Assert.AreEqual(4, update.Lines[3].OriginalLineNumber);
        Assert.AreEqual(5, update.Lines[4].OriginalLineNumber);
        Assert.AreEqual(6, update.Lines[5].OriginalLineNumber);
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Failure Case with current implementation)
    [TestMethod] public async Task FilteredUpdates_RapidLineEmits_AreThrottledToOneUpdate_WithCurrentImplementation()
    {
        // Arrange: Setup initial load with a filter that matches the incoming lines
        var initialLines = new List<string> { "Initial Line 0" };
        var filter = new SubstringFilter("Rapid"); // Filter to match the new lines
        await SetupInitialFileLoad(initialLines, filter, 0); // Setup helper advances scheduler and clears updates

        Assert.AreEqual(1, _logDocument.Count, "Arrange failure: LogDocument count incorrect.");
        Assert.AreEqual(0, _receivedUpdates.Count, "Arrange failure: Updates should be cleared by setup.");

        // Act: Emit multiple lines faster than the throttle time (100ms)
        int emitCount = 5;
        for (int i = 1; i <= emitCount; i++)
        {
            _mockLogSource.EmitLine($"Rapid Line {i}");
            // Advance virtual time by only 1ms between emits
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);
        }

        // Assert: No updates should have arrived *during* the rapid emission phase
        Assert.AreEqual(0, _receivedUpdates.Count, "No updates should be received before throttle window passes.");

        // Act: Advance the scheduler PAST the throttle window (e.g., 350ms)
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert (Current Failing Behavior):
        // 1. Only ONE update should be received due to throttling the full refilter trigger.
        Assert.AreEqual(1, _receivedUpdates.Count, "ERROR: Expected only one throttled update with the current implementation.");

        // 2. This single update should be a Replace update containing the FINAL state.
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        // It should contain lines matching "Rapid" from the *entire* document at the time of processing.
        Assert.AreEqual(emitCount, update.Lines.Count, "The single update should contain all matching emitted lines.");
        CollectionAssert.AreEqual(
            Enumerable.Range(1, emitCount).Select(i => $"Rapid Line {i}").ToList(),
            GetLinesText(update), // Using helper to extract text
            "Update content mismatch.");

        // 3. Verify LogDocument contains everything
        Assert.AreEqual(1 + emitCount, _logDocument.Count, "LogDocument should contain initial + emitted lines.");
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Success Case for future implementation)
    [TestMethod] [Ignore("Enable this test after implementing Approach 1 (Hybrid Pipeline)")] public async Task FilteredUpdates_RapidLineEmits_ProduceIncrementalAppends_WithFixedImplementation()
    {
        // Arrange: Setup initial load with a filter that matches the incoming lines
        var initialLines = new List<string> { "Initial Line 0" };
        var filter = new SubstringFilter("Rapid"); // Filter to match the new lines
        await SetupInitialFileLoad(initialLines, filter, 0); // Setup helper advances scheduler and clears updates

        Assert.AreEqual(1, _logDocument.Count);
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Emit multiple lines faster than the throttle time (100ms)
        int emitCount = 5;
        var expectedAppendedLines = new List<string>();
        for (int i = 1; i <= emitCount; i++)
        {
            string line = $"Rapid Line {i}";
            expectedAppendedLines.Add(line);
            _mockLogSource.EmitLine(line);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);
             // Advance enough here to trigger potential append buffer/throttle (adjust if needed)
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
        }

        // Advance scheduler enough to ensure ALL append processing finishes
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Generous time

        // Assert (Future Fixed Behavior - Approach 1):
        Assert.IsTrue(_receivedUpdates.Count > 1, "Expected multiple incremental updates.");

        // Verify the updates are of the expected type and contain the correct lines
        var allReceivedAppendedLines = _receivedUpdates
                                        .OfType<AppendFilteredUpdate>() // Filter for Append updates
                                        .SelectMany(u => u.Lines.Select(l => l.Text))
                                        .ToList();
        CollectionAssert.AreEqual(expectedAppendedLines, allReceivedAppendedLines, "Mismatch in aggregated appended lines.");
        Assert.IsTrue(_receivedUpdates.All(u => u is AppendFilteredUpdate), "Not all received updates were AppendFilteredUpdate.");

        // Verify LogDocument contains everything
        Assert.AreEqual(1 + emitCount, _logDocument.Count, "LogDocument should contain initial + emitted lines.");
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Interaction with context)
    [TestMethod] public async Task IncrementalMatch_TriggersFullRefilter_IncludesContext()
    {
        // Arrange: Setup initial load with context lines available
        var initialLines = new List<string> {
            "Context Line 1",       // LogDoc index 0, OrigNum 1
            "Some other info",      // LogDoc index 1, OrigNum 2
            "Another context line"  // LogDoc index 2, OrigNum 3
        };
        // Initial filter = FalseFilter, Context = 1. Loads LogDoc but results in empty display.
        await SetupInitialFileLoad(initialLines, initialFilter: new FalseFilter(), context: 1);
        Assert.AreEqual(0, _receivedUpdates.Count, "Setup should not produce updates with FalseFilter.");

        // Arrange: Now set the filter that *will* match the new line
        var filter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(filter, 1); // Keep Context = 1
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for this settings change
        Assert.AreEqual(1, _receivedUpdates.Count, "Setting MATCH filter should trigger one empty Replace update.");
        Assert.AreEqual(0, _receivedUpdates[0].Lines.Count);
        _receivedUpdates.Clear(); // Clear this update

        // Act: Emit a line that matches the filter AFTER initial load/filter setup
        _mockLogSource.EmitLine("Here is the MATCH line"); // LogDoc index 3, OrigNum 4
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Allow Select/Where processing in _logSubscription

        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for the *triggered* full refilter

        // Assert: Expect a REPLACE update triggered by the incremental match detection (current behavior)
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];

        // Assert: Content should include the new match and context from LogDocument
        Assert.AreEqual(2, update.Lines.Count);
        Assert.AreEqual("Another context line", update.Lines[0].Text); // Context from index 2
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber);
        Assert.IsTrue(update.Lines[0].IsContextLine);
        Assert.AreEqual("Here is the MATCH line", update.Lines[1].Text); // Match from index 3
        Assert.AreEqual(4, update.Lines[1].OriginalLineNumber);
        Assert.IsFalse(update.Lines[1].IsContextLine);
    }

    // Helper Filter for Setup
    private class FalseFilter : IFilter
    {
        public bool Enabled { get; set; } = true;
        public bool IsEditable => false;
        public string DisplayText => "FALSE";
        public string TypeText => "FalseFilter";
        public string Value { get; set; } = string.Empty;
        public bool IsMatch(string line) => false; // Always returns false
    }
}
