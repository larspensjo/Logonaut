// ===== File: tests\Logonaut.Core.Tests\LogFilterProcessorTests.cs =====

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency; // For IScheduler
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks; // For Task
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
    private MockLogSource _mockLogSource = null!;
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
        _mockLogSource = new MockLogSource();
        _logDocument = new LogDocument();
        _receivedUpdates = new List<FilteredUpdate>();
        _receivedError = null;
        _isCompleted = false;

        _processor = new LogFilterProcessor(
            _mockLogSource,
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
        _mockLogSource?.Dispose();
    }
    private void AddLineToLogDocument(string line)
    {
        // This method will be called by the processor, potentially on a background thread.
        // LogDocument handles its own locking.
        _logDocument.AppendLine(line);
    }

    private async Task SetupInitialFileLoad(List<string> initialLines, IFilter? initialFilter = null, int context = 0)
    {
        _processor.Reset();

        // 1. Set lines for the mock source to read
        _mockLogSource.LinesForInitialRead = initialLines;

        // 2. Simulate preparing the source (reads initial lines via callback, populates LogDoc)
        _logDocument.Clear(); // Clear explicitly BEFORE Prepare call
        long linesRead = await _mockLogSource.PrepareAndGetInitialLinesAsync("C:\\test.log", AddLineToLogDocument);
        Assert.AreEqual(initialLines.Count, linesRead, "MockLogSource did not report correct lines read.");
        Assert.AreEqual(initialLines.Count, _logDocument.Count, "LogDocument not populated correctly by Prepare callback.");

        // 3. Simulate starting monitoring (allows emitting new lines later)
        _mockLogSource.StartMonitoring();
        Assert.IsTrue(_mockLogSource.IsMonitoring, "MockLogSource should be monitoring after StartMonitoring.");

        // 4. Trigger the *first* filter application after preparation
        _processor.UpdateFilterSettings(initialFilter ?? new TrueFilter(), context);

        // 5. Advance scheduler past throttle/debounce time
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // 6. Clear updates received *during this setup*
        _receivedUpdates.Clear();
    }

    [TestMethod] public async Task FirstFilter_RunsAfterPrepareAndStart_And_UpdateFilterSettings()
    {
        // Arrange
        var initialLines = new List<string> { "Initial 1", "Initial 2" };
        _receivedUpdates.Clear();

        // Act: Use the updated async setup helper
        await SetupInitialFileLoad(initialLines, new TrueFilter(), 0);

        // Assert: LogDocument state verified inside helper now. Check no *further* updates.
        Assert.AreEqual(0, _receivedUpdates.Count, "No further updates expected immediately after setup.");
    }

    [TestMethod] public async Task Reset_ClearsProcessorState_LogDocumentPopulatedByPrepare()
    {
        // Arrange: Perform an initial load first
        await SetupInitialFileLoad(new List<string> { "Old Line 1" });
        Assert.AreEqual(1, _logDocument.Count); // Verify initial state

        // Act: Reset
        _processor.Reset();

        // Assert: Processor index reset, LogDocument *NOT* cleared by processor anymore
        Assert.AreEqual(1, _logDocument.Count, "LogDocument should NOT be cleared by processor Reset.");

        // Act: Load a new file using the setup helper (which calls Prepare again)
        var newLines = new List<string> { "New Line A" };
        await SetupInitialFileLoad(newLines); // Setup clears LogDoc before calling Prepare

        // Assert: Document contains only the new lines after setup
        Assert.AreEqual(1, _logDocument.Count);
        Assert.AreEqual("New Line A", _logDocument[0]);
    }

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
        var update = _receivedUpdates[0];
        Assert.AreEqual(4, update.Lines.Count);
        CollectionAssert.AreEqual(new List<string> { "Initial A", "Initial B", "Line C", "Line D" }, GetLinesText(update));
        Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
        Assert.AreEqual(4, update.Lines[3].OriginalLineNumber);
    }

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
        var update = _receivedUpdates[0];
        Assert.AreEqual(2, update.Lines.Count);
        Assert.AreEqual("MATCH 2", update.Lines[0].Text);
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber);
        Assert.AreEqual("MATCH 4", update.Lines[1].Text);
        Assert.AreEqual(5, update.Lines[1].OriginalLineNumber);
    }

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

    [TestMethod] public async Task UpdateFilterSettings_TriggersFullRefilter_OnCurrentDocumentContent()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Line 1 INFO", "Line 2 MATCH", "Line 3 INFO" };
        await SetupInitialFileLoad(initialLines); // Clears updates after setup

        // Act: Update filter settings to something specific
        var newFilter = new SubstringFilter("MATCH");
        _processor.UpdateFilterSettings(newFilter, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle

        // Assert: Should receive one *new* Replace update
        Assert.AreEqual(1, _receivedUpdates.Count);
        var update = _receivedUpdates[0];
        Assert.AreEqual(1, update.Lines.Count);
        Assert.AreEqual("Line 2 MATCH", update.Lines[0].Text);
        Assert.AreEqual(2, update.Lines[0].OriginalLineNumber);
    }


    [TestMethod] public async Task UpdateFilterSettings_RapidSubsequentCalls_ThrottledToOneUpdate()
    {
        // Arrange: Setup initial load
        await SetupInitialFileLoad(new List<string> { "Line 1 A", "Line 2 B" });

        var filterA = new SubstringFilter("A");
        var filterB = new SubstringFilter("B");

        // Act: Call UpdateFilterSettings rapidly
        _processor.UpdateFilterSettings(filterA, 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
        _processor.UpdateFilterSettings(new TrueFilter(), 0);
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
        _processor.UpdateFilterSettings(filterB, 0); // Final filter
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);

        // Assert: No updates yet
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Advance time *past* the throttle window
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: Only ONE update received, using the LAST filter settings (filterB)
        Assert.AreEqual(1, _receivedUpdates.Count);
        var update = _receivedUpdates[0];
        Assert.AreEqual(1, update.Lines.Count); // Filter B matches "Line 2 B"
        Assert.AreEqual("Line 2 B", update.Lines[0].Text);
        Assert.AreEqual(2, update.Lines[0].OriginalLineNumber);
    }

    [TestMethod] public async Task FullRefilter_IncludesContextLines()
    {
        // Arrange: Setup initial load
         var initialLines = new List<string> {
            "Context Before",  // Orig 1
            "MATCH Line",      // Orig 2
            "Context After 1", // Orig 3
            "Context After 2", // Orig 4
            "Another Line"     // Orig 5
        };
        await SetupInitialFileLoad(initialLines); // Loads with TrueFilter, clears updates

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
        Assert.IsFalse(update.Lines[1].IsContextLine); 
        Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);

        Assert.AreEqual("Context After 1", update.Lines[2].Text);
        Assert.IsTrue(update.Lines[2].IsContextLine);
        Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
    }


    [TestMethod] public async Task LogSourceError_PropagatesToFilteredUpdatesObserver() // Renamed test
    {
        // Arrange: Setup initial load successfully
        await SetupInitialFileLoad(new List<string> { "Line 1" });
        var expectedException = new InvalidOperationException("Log source failed");

        // Act: Emit error *after* initial load
        _mockLogSource.EmitError(expectedException); // Use MockLogSource method
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

        // Assert
        Assert.IsNotNull(_receivedError, "Error should have been received.");
        // Assert.IsInstanceOfType(_receivedError, typeof(InvalidOperationException)); // Check type
        // Assert.AreEqual(expectedException.Message, _receivedError.Message); // Check message
        Assert.IsTrue(_receivedError is InvalidOperationException || _receivedError?.InnerException is InvalidOperationException, "Unexpected error type");
        Assert.IsTrue(_receivedError!.Message.Contains("Log source failed") || _receivedError!.InnerException!.Message.Contains("Log source failed"), "Error message mismatch");

        int updateCountBeforeError = _receivedUpdates.Count;

        // Act: Try emitting after error
        _mockLogSource.EmitLine("After Error"); // Use MockLogSource method
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

        // Assert: No more updates after error
        Assert.AreEqual(updateCountBeforeError, _receivedUpdates.Count);
    }

    [TestMethod] public async Task Dispose_StopsProcessingAndCompletesObservable()
    {
        // Arrange: Setup initial load and process an emit
        await SetupInitialFileLoad(new List<string> { "Initial" });
        _mockLogSource.EmitLine("Append 1");
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
        Assert.AreEqual(1, _receivedUpdates.Count); // Ensure emit was processed
        _receivedUpdates.Clear(); // Clear updates before disposal

        // Act
        _processor.Dispose(); // Dispose the processor

        // Try emitting after processor disposal. This should NOT throw on the source itself.
        // Assert.ThrowsException<ObjectDisposedException>(() => _mockLogSource.EmitLine("Append 2 (after dispose)")); // <<<< REMOVE THIS LINE
        _mockLogSource.EmitLine("Append 2 (after processor dispose)"); // This call is valid on the mock source

        // Advance scheduler to see if any processing happens (it shouldn't)
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

        // Assert
        Assert.AreEqual(0, _receivedUpdates.Count, "No updates should be received after Processor Dispose.");
        Assert.IsTrue(_isCompleted, "FilteredUpdates observable should complete on Processor Dispose.");

        // Optional: Assert that the source itself was NOT disposed by the processor
        Assert.IsFalse(_mockLogSource.IsDisposed, "Processor should NOT dispose the injected log source.");
    }

    [TestMethod] public async Task IncrementalMatch_TriggersFullRefilter_IncludesContext()
    {
        // Arrange: Setup initial load with context lines available
        // Use a filter initially that *won't* match the initial lines, but set context lines.
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
        _mockLogSource.EmitLine("Here is the MATCH line"); // Use mock source emit
        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Allow Select/Where processing in _logSubscription

        _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle for the *triggered* full refilter

        // Assert: Expect a REPLACE update triggered by the incremental match detection
        Assert.AreEqual(1, _receivedUpdates.Count);
        var update = _receivedUpdates[0];

        // Assert: Content should include the new match and context from LogDocument
        Assert.AreEqual(2, update.Lines.Count);
        Assert.AreEqual("Another context line", update.Lines[0].Text);
        Assert.AreEqual(3, update.Lines[0].OriginalLineNumber);
        Assert.IsTrue(update.Lines[0].IsContextLine);
        Assert.AreEqual("Here is the MATCH line", update.Lines[1].Text);
        Assert.AreEqual(4, update.Lines[1].OriginalLineNumber);
        Assert.IsFalse(update.Lines[1].IsContextLine);
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
