using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency; // For IScheduler
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Microsoft.Reactive.Testing; // Essential for TestScheduler
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.TestUtils; // Use mocks from here

namespace Logonaut.Core.Tests;
[TestClass]
public class LogFilterProcessorTests
{
    private TestScheduler _scheduler = null!; // For controlling virtual time
    private MockLogTailerService _mockTailer = null!;
    private LogDocument _logDocument = null!;
    private LogFilterProcessor _processor = null!;
    private List<FilteredUpdate> _receivedUpdates = null!; // Capture results
    private Exception? _receivedError = null; // Capture errors
    private bool _isCompleted = false; // Capture completion
    private IDisposable? _subscription;

    // Helper
    private List<string> GetLinesText(FilteredUpdate update) => update.Lines.Select(l => l.Text).ToList();

    [TestInitialize]
    public void TestInitialize()
    {
        _scheduler = new TestScheduler();
        _mockTailer = new MockLogTailerService();
        _logDocument = new LogDocument();
        _receivedUpdates = new List<FilteredUpdate>();
        _receivedError = null;
        _isCompleted = false;

        _processor = new LogFilterProcessor(
            _mockTailer,
            _logDocument,
            new ImmediateSynchronizationContext(),
            AddLineToLogDocument,
            _scheduler
        );

        _subscription = _processor.FilteredUpdates.Subscribe(
            update => _receivedUpdates.Add(update),
            ex => _receivedError = ex,
            () => _isCompleted = true
        );
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _subscription?.Dispose();
        _processor?.Dispose();
        _mockTailer?.Dispose();
    }
    private void AddLineToLogDocument(string line)
    {
        // This method will be called by the processor, potentially on a background thread.
        // LogDocument handles its own locking.
        _logDocument.AppendLine(line);
        // Optional: Could add tracing here if needed
        // System.Diagnostics.Debug.WriteLine($"---> MainViewModel: Added line to LogDoc via callback: {line.Substring(0, Math.Min(line.Length, 20))}");
    }

    private void SetupInitialFileLoad(List<string> initialLines, IFilter? initialFilter = null, int context = 0)
    {
        _processor.Reset();
        _mockTailer.LinesForInitialRead = initialLines;
        _logDocument.Clear(); // Clear before setup
        var task = _mockTailer.ChangeFileAsync("C:\\test.log", AddLineToLogDocument); task.Wait();
        Assert.AreEqual(initialLines.Count, _logDocument.Count, "LogDocument not populated correctly by mock ChangeFileAsync.");
        _processor.UpdateFilterSettings(initialFilter ?? new TrueFilter(), context);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Advance past throttle
        _receivedUpdates.Clear(); // Clear initial update for subsequent assertions
    }

    // --- Corrected Tests ---

    [TestMethod] public void FirstFilter_RunsAfterChangeFile_And_UpdateFilterSettings()
    {
        // Arrange
        var initialLines = new List<string> { "Initial 1", "Initial 2" };
        _receivedUpdates.Clear(); // Clear before setup

        // Act: Use the setup helper
        SetupInitialFileLoad(initialLines, new TrueFilter(), 0);

        // Assert: Setup helper already cleared initial update, check LogDocument
        Assert.AreEqual(2, _logDocument.Count);
        Assert.AreEqual("Initial 1", _logDocument[0]);
        Assert.AreEqual("Initial 2", _logDocument[1]);

        // No further updates expected without more actions
            Assert.AreEqual(0, _receivedUpdates.Count, "No further updates expected immediately after initial load.");
    }

    [TestMethod] public void Reset_ClearsProcessorState_LogDocumentPopulatedByChangeFile()
    {
        // Arrange: Perform an initial load first
        SetupInitialFileLoad(new List<string> { "Old Line 1" });
        Assert.AreEqual(1, _logDocument.Count); // Verify initial state

        // Act: Reset
        _processor.Reset();

        // Assert: Processor index reset, LogDocument *NOT* cleared by processor anymore
        // (ViewModel/TailerService clears it before ChangeFileAsync)
        Assert.AreEqual(1, _logDocument.Count, "LogDocument should NOT be cleared by processor Reset.");
        // We can't easily check _currentLineIndex, but ResetCallCount can be checked if mock processor exposed it.

            // Act: Load a new file to confirm LogDocument is cleared by ChangeFileAsync
            var newLines = new List<string> { "New Line A" };
            SetupInitialFileLoad(newLines);

            // Assert: Document contains only the new lines
            Assert.AreEqual(1, _logDocument.Count);
            Assert.AreEqual("New Line A", _logDocument[0]);
    }

    [TestMethod] public void NewLines_TriggerSingleThrottledReplaceUpdate_WithAllLines()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Initial A", "Initial B" };
        SetupInitialFileLoad(initialLines, new TrueFilter(), 0); // Clears _receivedUpdates

        // Act: Emit lines *after* initial load setup
        _mockTailer.EmitLine("Line C");
        _mockTailer.EmitLine("Line D");

        // Assert: No updates yet because of throttle
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Advance scheduler *past* throttle time
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: LogDocument contains all lines
        Assert.AreEqual(4, _logDocument.Count, "LogDocument should contain initial and emitted lines.");
        Assert.AreEqual("Initial A", _logDocument[0]);
        Assert.AreEqual("Initial B", _logDocument[1]);
        Assert.AreEqual("Line C", _logDocument[2]);
        Assert.AreEqual("Line D", _logDocument[3]);

        // Assert: ONE Replace update received containing ALL lines
        Assert.AreEqual(1, _receivedUpdates.Count, "Should have received one Replace update.");
        var update = _receivedUpdates[0];
        Assert.AreEqual(4, update.Lines.Count, "Replace update should contain all lines.");

        // Verify content and OriginalLineNumbers (based on final LogDocument state)
        CollectionAssert.AreEqual(new List<string> { "Initial A", "Initial B", "Line C", "Line D" }, GetLinesText(update));
        Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
        Assert.AreEqual(4, update.Lines[3].OriginalLineNumber);
    }

    [TestMethod] public void NewLines_TriggerFilteredReplaceUpdate_OnEntireDocument() 
    {
        // Arrange: Setup initial load
        var initialLines = new List<string> { "Initial INFO" };
        SetupInitialFileLoad(initialLines); // Uses TrueFilter initially, clears updates

        // Arrange: Set specific filter *after* initial load
        var filter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(filter, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for filter change
        // This update should contain 0 lines as "MATCH" isn't in "Initial INFO"
        Assert.AreEqual(1, _receivedUpdates.Count, "Setting MATCH filter should trigger Replace update.");
        Assert.AreEqual(0, _receivedUpdates.Last().Lines.Count, "Filter change update should be empty.");
        _receivedUpdates.Clear(); // Clear the filter update

        // Act: Emit new lines
        _mockTailer.EmitLine("IGNORE 1");          // LogDoc index 1, Orig 2
        _mockTailer.EmitLine("MATCH 2");           // LogDoc index 2, Orig 3
        _mockTailer.EmitLine("IGNORE 3");          // LogDoc index 3, Orig 4
        _mockTailer.EmitLine("MATCH 4");           // LogDoc index 4, Orig 5

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
        Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Replace update.");
        var update = _receivedUpdates[0];
        Assert.AreEqual(2, update.Lines.Count, "Filtered update should contain only matching lines.");

        // Verify content and OriginalLineNumbers based on final LogDocument state
        Assert.AreEqual("MATCH 2", update.Lines[0].Text);
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber); // It's the 3rd line overall

        Assert.AreEqual("MATCH 4", update.Lines[1].Text);
        Assert.AreEqual(5, update.Lines[1].OriginalLineNumber); // It's the 5th line overall
    }

    [TestMethod] public void NewLines_TriggerReplaceUpdate_UsesCorrectOriginalLineNumbers()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Line 1", "Line 2", "Line 3" };
        SetupInitialFileLoad(initialLines, new TrueFilter(), 0); // Clears updates

        // Act: Emit new lines, some before throttle, some after (but only one trigger matters)
        _mockTailer.EmitLine("Line A"); // Triggers full refilter process...
        _mockTailer.EmitLine("Line B");
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // Not enough for throttle
        _mockTailer.EmitLine("Line C"); // Triggers again...
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Advance PAST throttle window (relative to first emit)

        // Assert: LogDocument contains all lines
        Assert.AreEqual(6, _logDocument.Count);
        Assert.AreEqual("Line 1", _logDocument[0]);
        Assert.AreEqual("Line 2", _logDocument[1]);
        Assert.AreEqual("Line 3", _logDocument[2]);
        Assert.AreEqual("Line A", _logDocument[3]);
        Assert.AreEqual("Line B", _logDocument[4]);
        Assert.AreEqual("Line C", _logDocument[5]);

        // Assert: Received ONE Replace update with ALL lines and correct OriginalLineNumbers
        Assert.AreEqual(1, _receivedUpdates.Count, "Should have received only one throttled Replace update.");
        var update = _receivedUpdates[0];
        Assert.AreEqual(6, update.Lines.Count);

        CollectionAssert.AreEqual(new List<string> { "Line 1", "Line 2", "Line 3", "Line A", "Line B", "Line C" }, GetLinesText(update));
        Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
        Assert.AreEqual(4, update.Lines[3].OriginalLineNumber);
        Assert.AreEqual(5, update.Lines[4].OriginalLineNumber);
        Assert.AreEqual(6, update.Lines[5].OriginalLineNumber);
    }

    [TestMethod] public void UpdateFilterSettings_TriggersFullRefilter_OnInitialDocumentContent()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Line 1 INFO", "Line 2 MATCH", "Line 3 INFO" };
        SetupInitialFileLoad(initialLines);

        // Act: Update filter settings to something specific
        var newFilter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(newFilter, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle

        // Assert: Should receive one *new* Replace update
        Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Replace update from filter change.");
        var update = _receivedUpdates[0];
        Assert.AreEqual(1, update.Lines.Count);
        Assert.AreEqual("Line 2 MATCH", update.Lines[0].Text);
        // OriginalLineNumber comes from FilterEngine based on LogDocument state
        Assert.AreEqual(2, update.Lines[0].OriginalLineNumber, "OriginalLineNumber mismatch in filtered result.");
    }


    [TestMethod] public void UpdateFilterSettings_RapidSubsequentCalls_ThrottledToOneUpdate() // Renamed & Logic Corrected
    {
        // Arrange: Setup initial load
        SetupInitialFileLoad(new List<string> { "Line 1 A", "Line 2 B" });

        var filterA = new SubstringFilter("A");
        var filterB = new SubstringFilter("B"); // The last filter applied

        // Act: Call UpdateFilterSettings rapidly, less than Throttle time (300ms)
        _processor.UpdateFilterSettings(filterA, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
        _processor.UpdateFilterSettings(new TrueFilter(), 0); // Intermediate filter
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
        _processor.UpdateFilterSettings(filterB, 0); // Final filter within throttle window
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);

        // Assert: No updates yet due to throttle
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Advance time *past* the throttle window (300ms)
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: Only ONE update received, using the LAST filter settings
        Assert.AreEqual(1, _receivedUpdates.Count, "Should receive only one throttled Replace update.");
        var update = _receivedUpdates[0];
        Assert.AreEqual(1, update.Lines.Count); // Filter B matches "Line 2 B"
        Assert.AreEqual("Line 2 B", update.Lines[0].Text);
        Assert.AreEqual(2, update.Lines[0].OriginalLineNumber); // Original index 2
    }


    [TestMethod] public void FullRefilter_IncludesContextLines()
    {
        // Arrange: Setup initial load
        var initialLines = new List<string> {
            "Context Before",  // Orig 1
            "MATCH Line",      // Orig 2
            "Context After 1", // Orig 3
            "Context After 2", // Orig 4
            "Another Line"     // Orig 5
        };
        SetupInitialFileLoad(initialLines); // Loads with TrueFilter

        // Act: Update filter *once* with context lines
        _processor.UpdateFilterSettings(new SubstringFilter("MATCH"), 1); // 1 context line
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle

        // Assert
        Assert.AreEqual(1, _receivedUpdates.Count);
        var update = _receivedUpdates[0];
        Assert.AreEqual(3, update.Lines.Count);
        Assert.AreEqual("Context Before", update.Lines[0].Text);
        Assert.IsTrue(update.Lines[0].IsContextLine);
        Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);

        Assert.AreEqual("MATCH Line", update.Lines[1].Text);
        Assert.IsFalse(update.Lines[1].IsContextLine); // The match
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);

        Assert.AreEqual("Context After 1", update.Lines[2].Text);
        Assert.IsTrue(update.Lines[2].IsContextLine);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
    }


    [TestMethod] public void TailerError_PropagatesToFilteredUpdatesObserver()
    {
            // Arrange: Setup initial load successfully
            SetupInitialFileLoad(new List<string> { "Line 1" });
            var expectedException = new InvalidOperationException("Tailer failed");

            // Act: Emit error *after* initial load
            _mockTailer.EmitError(expectedException);
            // Advance scheduler slightly for safety, though errors often bypass TestScheduler timing
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

            // Assert
            Assert.IsNotNull(_receivedError, "Error should have been received.");
            // Rx might wrap exceptions depending on the pipeline
            // Assert.IsInstanceOfType(_receivedError, typeof(InvalidOperationException));
            // Assert.AreEqual(expectedException.Message, _receivedError.Message);
            Assert.IsTrue(_receivedError is InvalidOperationException || _receivedError?.InnerException is InvalidOperationException, "Unexpected error type");
            Assert.IsTrue(_receivedError!.Message.Contains("Tailer failed") || _receivedError!.InnerException!.Message.Contains("Tailer failed"), "Error message mismatch");
            int updateCountBeforeError = _receivedUpdates.Count; // Capture count before error

            // Act: Try emitting after error
            _mockTailer.EmitLine("After Error");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

            // Assert: No more updates after error
            Assert.AreEqual(updateCountBeforeError, _receivedUpdates.Count, "No regular updates should be received after error.");
    }

    [TestMethod] public void Dispose_StopsProcessingAndCompletesObservable()
    {
            // Arrange: Setup initial load and process an append
            SetupInitialFileLoad(new List<string> { "Initial" });
            _mockTailer.EmitLine("Append 1");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
            Assert.AreEqual(1, _receivedUpdates.Count); // Ensure Append was processed
            _receivedUpdates.Clear();

            // Act
            _processor.Dispose();

            // Try emitting after disposal
            _mockTailer.EmitLine("Append 2 (after dispose)");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

            // Assert
            Assert.AreEqual(0, _receivedUpdates.Count, "No updates should be received after Dispose.");
            Assert.IsTrue(_isCompleted, "FilteredUpdates observable should complete on Dispose.");
            // Assert.IsTrue(_mockTailer.IsDisposed); // Processor should *not* dispose injected dependencies
    }

    [TestMethod] public void IncrementalMatch_TriggersFullRefilter_IncludesContext()
    {
        // Arrange: Setup initial load with context lines available
        // Use a filter initially that *won't* match the initial lines, but set context lines.
        var initialLines = new List<string> {
            "Context Line 1",       // LogDoc index 0, OrigNum 1
            "Some other info",      // LogDoc index 1, OrigNum 2
            "Another context line"  // LogDoc index 2, OrigNum 3
        };
        // Initial filter = FalseFilter, Context = 1. This loads LogDoc but results in empty display.
        SetupInitialFileLoad(initialLines, initialFilter: new FalseFilter(), context: 1);
        Assert.AreEqual(0, _receivedUpdates.Count, "Setup should not produce updates with FalseFilter.");

        // Arrange: Now set the filter that *will* match the new line
        var filter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(filter, 1); // Keep Context = 1
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for this settings change
        // The Replace update from this filter change will be empty as "MATCH" isn't in initialLines
        Assert.AreEqual(1, _receivedUpdates.Count, "Setting the MATCH filter should trigger one empty Replace update.");
        Assert.AreEqual(0, _receivedUpdates[0].Lines.Count);
        _receivedUpdates.Clear(); // Clear this update

        // Act: Emit a line that matches the filter AFTER initial load/filter setup
        _mockTailer.EmitLine("Here is the MATCH line"); // Processor index 1 (since reset)
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Allow buffer/Where processing in _logSubscription
                                                                // This triggers the _filterChangeTriggerSubject

        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for the *triggered* full refilter

        // Assert: Expect a REPLACE update triggered by the incremental match detection
        Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Replace update triggered by incremental match.");
        var update = _receivedUpdates[0];

        // Assert: Content should include the new match and context from LogDocument
        // The full filter runs on ["Context Line 1", "Some other info", "Another context line"]
        // and the logical new line "Here is the MATCH line".
        // FilterEngine.ApplyFilters correctly handles context around the new line.
        // Note: FilterEngine uses 1-based indices from LogDocument for OriginalLineNumber.
        // The processor's internal index (like 1 for the emitted line) is NOT used for the OriginalLineNumber in the output.
        Assert.AreEqual(2, update.Lines.Count, "Update should contain match and preceding context.");

        Assert.AreEqual("Another context line", update.Lines[0].Text); // Context from LogDoc[2]
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber);       // Original line number from LogDocument
        Assert.IsTrue(update.Lines[0].IsContextLine, "Line 1 should be context.");

        Assert.AreEqual("Here is the MATCH line", update.Lines[1].Text); // The new matching line
        Assert.AreEqual(4, update.Lines[1].OriginalLineNumber);       // Its effective original number is *after* initial lines
        Assert.IsFalse(update.Lines[1].IsContextLine, "Line 2 should be the match.");
    }

    // Helper Filter for Setup
    public class FalseFilter : IFilter
    {
        public bool Enabled { get; set; } = true;
        public bool IsEditable => false;
        public string DisplayText => "FALSE";
        public string TypeText => "FalseFilter";
        public string Value { get; set; } = string.Empty;
        public bool IsMatch(string line) => false; // Always returns false
    }
}
