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
/// from the log source, including buffering, incremental filtering, and emitting Append updates.
/// </summary>
[TestClass] public class LogFilterProcessor_NewLineProcessingTests : LogFilterProcessorTestBase
{
    // Verifies: [ReqFilterEfficientRealTimev1] (Append update type)
    [TestMethod] public async Task NewLines_MatchingTrueFilter_TriggerAppendUpdate_WithOnlyNewLines() // Renamed
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Initial A", "Initial B" };
        await SetupInitialFileLoad(initialLines, new TrueFilter(), 0); // Clears _receivedUpdates

        // Act: Emit lines *after* setup
        _mockLogSource.EmitLine("Line C"); // Should match TrueFilter
        _mockLogSource.EmitLine("Line D"); // Should match TrueFilter

        // Assert: No updates yet because of buffer/throttle
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Advance scheduler *past* buffer/throttle time
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Ensure buffer flushes

        // Assert: LogDocument contains all lines
        Assert.AreEqual(4, _logDocument.Count);
        Assert.AreEqual("Line C", _logDocument[2]);
        Assert.AreEqual("Line D", _logDocument[3]);

        // Assert: ONE Append update received containing ONLY the NEW lines
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(AppendFilteredUpdate));
        var update = (AppendFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(2, update.Lines.Count, "Append update should only contain new lines C and D.");
        CollectionAssert.AreEqual(new List<string> { "Line C", "Line D" }, GetLinesText(update));
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber); // Line C was 3rd overall
        Assert.AreEqual(4, update.Lines[1].OriginalLineNumber); // Line D was 4th overall
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Filtering applied to new lines), [ReqFilterDisplayMatchingLinesv1]
    [TestMethod] public async Task NewLines_TriggerFilteredAppendUpdate_OnlyMatchingNewLines() // Renamed
    {
        // Arrange: Setup initial load
        var initialLines = new List<string> { "Initial INFO" };
        await SetupInitialFileLoad(initialLines); // Uses TrueFilter initially, clears updates

        // Arrange: Set specific filter *after* initial load
        var filter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(filter, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for filter change (Replace)
        Assert.AreEqual(1, _receivedUpdates.Count); // One Replace update from filter change
        Assert.AreEqual(0, _receivedUpdates.Last().Lines.Count); // Should be empty
        _receivedUpdates.Clear(); // Clear this update

        // Act: Emit new lines
        _mockLogSource.EmitLine("IGNORE 1");
        _mockLogSource.EmitLine("MATCH 2"); // Should match
        _mockLogSource.EmitLine("IGNORE 3");
        _mockLogSource.EmitLine("MATCH 4"); // Should match

        // Advance scheduler *past* buffer/throttle time for the new lines
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: LogDocument contains all lines
        Assert.AreEqual(5, _logDocument.Count);

        // Assert: Received ONE Append update filtered ONLY on the *newly emitted* lines
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(AppendFilteredUpdate));
        var update = (AppendFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(2, update.Lines.Count, "Append update should contain only the two new MATCH lines.");
        Assert.AreEqual("MATCH 2", update.Lines[0].Text);
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber); // Was 3rd line overall
        Assert.AreEqual("MATCH 4", update.Lines[1].Text);
        Assert.AreEqual(5, update.Lines[1].OriginalLineNumber); // Was 5th line overall
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqDisplayOriginalLineNumbersv1]
    [TestMethod] public async Task NewLines_TriggerAppendUpdate_UsesCorrectOriginalLineNumbers() // Renamed
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Line 1", "Line 2" }; // Start with 2 lines
        await SetupInitialFileLoad(initialLines, new TrueFilter(), 0); // Clears updates

        // Act: Emit new lines
        _mockLogSource.EmitLine("Line A"); // Overall 3rd line
        _mockLogSource.EmitLine("Line B"); // Overall 4th line
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Past buffer/throttle window

        // Assert: LogDocument contains all lines
        Assert.AreEqual(4, _logDocument.Count);

        // Assert: Received ONE Append update with NEW lines and correct OriginalLineNumbers
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(AppendFilteredUpdate));
        var update = (AppendFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(2, update.Lines.Count);
        CollectionAssert.AreEqual(new List<string> { "Line A", "Line B" }, GetLinesText(update));
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber); // Line A was 3rd
        Assert.AreEqual(4, update.Lines[1].OriginalLineNumber); // Line B was 4th
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Correct append behavior with buffering)
    [TestMethod] public async Task FilteredUpdates_RapidLineEmits_ProduceIncrementalAppends() // Renamed and enabled
    {
        // Arrange: Setup initial load with a filter that matches the incoming lines
        var initialLines = new List<string> { "Initial Line 0" };
        var filter = new SubstringFilter("Rapid"); // Filter to match the new lines
        await SetupInitialFileLoad(initialLines, filter, 0); // Setup helper advances scheduler and clears updates

        Assert.AreEqual(1, _logDocument.Count);
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Emit multiple lines, potentially flushing buffer multiple times
        int emitCount = 75; // More than buffer size
        var expectedAppendedLines = new List<string>();
        for (int i = 1; i <= emitCount; i++)
        {
            string line = $"Rapid Line {i}";
            expectedAppendedLines.Add(line);
            _mockLogSource.EmitLine(line);
            // Advance time slightly, enough to potentially trigger time buffer if count not reached
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks);
        }

        // Advance scheduler enough to ensure ALL append processing finishes
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Generous time

        // Assert: Check received updates
        Assert.IsTrue(_receivedUpdates.Count > 0, "Expected at least one incremental update."); // Could be 1 or more depending on buffer timing
        Assert.IsTrue(_receivedUpdates.All(u => u is AppendFilteredUpdate), "Not all received updates were AppendFilteredUpdate.");

        // Verify the aggregated content of all Append updates matches the expected lines
        var allReceivedAppendedLines = _receivedUpdates
                                        .OfType<AppendFilteredUpdate>() // Filter for Append updates
                                        .SelectMany(u => u.Lines.Select(l => l.Text))
                                        .ToList();
        CollectionAssert.AreEqual(expectedAppendedLines, allReceivedAppendedLines, "Mismatch in aggregated appended lines.");

        // Verify original line numbers in the aggregated list
        var allReceivedOriginalNumbers = _receivedUpdates
                                        .OfType<AppendFilteredUpdate>()
                                        .SelectMany(u => u.Lines.Select(l => l.OriginalLineNumber))
                                        .ToList();
        var expectedOriginalNumbers = Enumerable.Range(2, emitCount).ToList(); // Starts from 2 (after initial line 1)
        CollectionAssert.AreEqual(expectedOriginalNumbers, allReceivedOriginalNumbers, "Mismatch in aggregated original line numbers.");


        // Verify LogDocument contains everything
        Assert.AreEqual(1 + emitCount, _logDocument.Count, "LogDocument should contain initial + emitted lines.");
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Interaction with context)
    [TestMethod] public async Task NewLineMatch_TriggersAppendUpdate_IncludesContext() // Renamed
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
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for this settings change (Replace)
        Assert.AreEqual(1, _receivedUpdates.Count); // Replace update (empty)
        Assert.AreEqual(0, _receivedUpdates[0].Lines.Count);
        _receivedUpdates.Clear(); // Clear this update

        // Act: Emit a line that matches the filter AFTER initial load/filter setup
        _mockLogSource.EmitLine("Here is the MATCH line"); // LogDoc index 3, OrigNum 4
        // Advance scheduler enough for the *incremental* pipeline buffer/throttle
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: Expect an APPEND update triggered by the incremental match
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(AppendFilteredUpdate));
        var update = (AppendFilteredUpdate)_receivedUpdates[0];

        // Assert: Content should include the new match and context from LogDocument
        Assert.AreEqual(2, update.Lines.Count);
        // Context line comes first because it has a lower original number
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
