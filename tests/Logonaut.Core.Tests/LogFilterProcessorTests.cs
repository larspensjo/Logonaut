// Logonaut.Core.Tests/LogFilterProcessorTests.cs
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
using Logonaut.Core.Tests; // Assuming MockLogTailerService is accessible here
using Logonaut.TestUtils;

namespace Logonaut.Core.Tests
{
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

        // Helper to get just the text from updates for easier comparison
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

            // Use a simple SyncContext for tests. Testing the actual UI marshalling
            // is complex; we focus on the logic *before* ObserveOn(_uiContext).
            // The IScheduler parameter in the constructor is key for testing time.
            _processor = new LogFilterProcessor(
                _mockTailer,
                _logDocument,
                new ImmediateSynchronizationContext(), // Basic context for test execution
                _scheduler // Inject the TestScheduler!
            );

            // Subscribe to capture results, errors, and completion
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
            _mockTailer?.Dispose(); // Dispose the mock if needed
        }

        // --- Test Cases ---

        [TestMethod]
        public void Reset_ClearsDocumentAndEmitsEmptyReplaceUpdateAfterDebounce()
        {
            // Arrange
            _logDocument.AppendLine("Existing Line 1");
            _processor.UpdateFilterSettings(new SubstringFilter("test"), 1);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Process initial filter setting
            _receivedUpdates.Clear(); // Clear initial update AND the one from filter setting

            // Act
            _processor.Reset();

            // Assert: LogDocument cleared immediately
            Assert.AreEqual(0, _logDocument.Count, "LogDocument should be cleared immediately by Reset.");

            // Assert: Check for the immediate Post update (Needs ImmediateSynchronizationContext to run Post synchronously)
            // IF using basic SynchronizationContext, this update might not appear synchronously here.
            // Assuming ImmediateSynchronizationContext or similar for robust testing:
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one immediate update from Reset's Post.");
            Assert.AreEqual(UpdateType.Replace, _receivedUpdates[0].Type, "Immediate Update type should be Replace.");
            Assert.AreEqual(0, _receivedUpdates[0].Lines.Count, "Immediate Update should contain zero lines.");

            // Act: Advance scheduler PAST the debounce time
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

            // Assert: Check total updates and the debounced one
            Assert.AreEqual(2, _receivedUpdates.Count, "Should have 2 total updates after Reset (Post + Debounce)."); // FIX: Expect 2 total now
            var debouncedUpdate = _receivedUpdates[1]; // FIX: Check the second update
            Assert.AreEqual(UpdateType.Replace, debouncedUpdate.Type, "Debounced update type should be Replace.");
            Assert.AreEqual(0, debouncedUpdate.Lines.Count, "Debounced update should contain zero lines.");
        }

        [TestMethod]
        public void Incremental_AppendsLinesAndAssignsCorrectOriginalNumbers()
        {
            // Arrange
            _processor.UpdateFilterSettings(new TrueFilter(), 0); // Match everything
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Process initial filter
            _receivedUpdates.Clear();

            // Act
            _mockTailer.EmitLine("Line A"); // Orig 1
            _mockTailer.EmitLine("Line B"); // Orig 2
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Advance past buffer time (100ms)

            _mockTailer.EmitLine("Line C"); // Orig 3
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Advance past buffer time

            // Assert Document Content
            Assert.AreEqual(3, _logDocument.Count, "LogDocument should have 3 lines.");
            Assert.AreEqual("Line A", _logDocument[0]);
            Assert.AreEqual("Line B", _logDocument[1]);
            Assert.AreEqual("Line C", _logDocument[2]);

            // Assert Received Updates
            Assert.AreEqual(2, _receivedUpdates.Count, "Should have received two Append updates.");

            // Check first update (Lines A & B)
            var update1 = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update1.Type);
            Assert.AreEqual(2, update1.Lines.Count);
            Assert.AreEqual(1, update1.Lines[0].OriginalLineNumber);
            Assert.AreEqual("Line A", update1.Lines[0].Text);
            Assert.IsFalse(update1.Lines[0].IsContextLine);
            Assert.AreEqual(2, update1.Lines[1].OriginalLineNumber);
            Assert.AreEqual("Line B", update1.Lines[1].Text);
            Assert.IsFalse(update1.Lines[1].IsContextLine);

            // Check second update (Line C)
            var update2 = _receivedUpdates[1];
            Assert.AreEqual(UpdateType.Append, update2.Type);
            Assert.AreEqual(1, update2.Lines.Count);
            Assert.AreEqual(3, update2.Lines[0].OriginalLineNumber);
            Assert.AreEqual("Line C", update2.Lines[0].Text);
            Assert.IsFalse(update2.Lines[0].IsContextLine);
        }

        [TestMethod]
        public void Incremental_AppliesCurrentFilterToBufferedLines()
        {
            // Arrange
            _processor.UpdateFilterSettings(new SubstringFilter("MATCH"), 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Process initial filter
            _receivedUpdates.Clear();

            // Act
            _mockTailer.EmitLine("IGNORE 1");  // Orig 1
            _mockTailer.EmitLine("MATCH 2");   // Orig 2
            _mockTailer.EmitLine("IGNORE 3");  // Orig 3
            _mockTailer.EmitLine("MATCH 4");   // Orig 4
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process buffer (100ms)

            // Assert
            Assert.AreEqual(4, _logDocument.Count, "LogDocument should contain all emitted lines.");
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Append update.");

            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update.Type);
            Assert.AreEqual(2, update.Lines.Count, "Update should contain only matching lines.");

            Assert.AreEqual(2, update.Lines[0].OriginalLineNumber); // Original line number of "MATCH 2"
            Assert.AreEqual("MATCH 2", update.Lines[0].Text);
            Assert.IsFalse(update.Lines[0].IsContextLine);

            Assert.AreEqual(4, update.Lines[1].OriginalLineNumber); // Original line number of "MATCH 4"
            Assert.AreEqual("MATCH 4", update.Lines[1].Text);
            Assert.IsFalse(update.Lines[1].IsContextLine);
        }


        [TestMethod]
        public void UpdateFilterSettings_TriggersFullRefilterWithLatestSettings()
        {
            // Arrange: Add some initial lines and process them
            _processor.UpdateFilterSettings(new TrueFilter(), 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
            _mockTailer.EmitLine("Line 1 INFO");    // Orig 1
            _mockTailer.EmitLine("Line 2 MATCH");   // Orig 2
            _mockTailer.EmitLine("Line 3 INFO");    // Orig 3
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process initial lines
            _receivedUpdates.Clear(); // Clear initial Append updates

            // Act: Update filter settings and advance past debounce time
            var newFilter = new SubstringFilter("MATCH");
            _processor.UpdateFilterSettings(newFilter, 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Advance past debounce (300ms)

            // Assert
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Replace update.");
            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Replace, update.Type);
            Assert.AreEqual(1, update.Lines.Count, "Replace update should contain only matching line.");
            Assert.AreEqual(2, update.Lines[0].OriginalLineNumber);
            Assert.AreEqual("Line 2 MATCH", update.Lines[0].Text);
            Assert.IsFalse(update.Lines[0].IsContextLine);
        }

        [TestMethod]
        public void UpdateFilterSettings_DebouncesRapidCalls()
        {
            // Arrange: Add initial lines
            _processor.UpdateFilterSettings(new TrueFilter(), 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
            _mockTailer.EmitLine("Line 1 A"); // Orig 1
            _mockTailer.EmitLine("Line 2 B"); // Orig 2
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
            _receivedUpdates.Clear();

            var filterA = new SubstringFilter("A");
            var filterB = new SubstringFilter("B");

            // Act: Call UpdateFilterSettings rapidly
            _processor.UpdateFilterSettings(filterA, 0); // Time: T0 (scheduler time)
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // Time: T0 + 100ms
            _processor.UpdateFilterSettings(filterB, 0); // Time: T0 + 100ms
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // Time: T0 + 200ms

            // Assert: No update yet, debounce timer restarted at T0 + 100ms
            Assert.AreEqual(0, _receivedUpdates.Count, "No update should occur before debounce period expires.");

            // Act: Advance past the debounce period started at T0 + 100ms (100 + 300 = 400ms needed from T0+100)
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks); // Total Time: T0 + 450ms

            // Assert: Should have received ONE update using the LAST filter (filterB)
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive exactly one update after debounce.");
            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Replace, update.Type);
            Assert.AreEqual(1, update.Lines.Count, "Update should contain line matching filter B.");
            Assert.AreEqual("Line 2 B", update.Lines[0].Text);
            Assert.AreEqual(2, update.Lines[0].OriginalLineNumber);
        }

        [TestMethod]
        public void FullRefilter_IncludesContextLines()
        {
            // Arrange: Add lines
            _processor.UpdateFilterSettings(new TrueFilter(), 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
            _mockTailer.EmitLine("Context Before"); // Orig 1
            _mockTailer.EmitLine("MATCH Line");     // Orig 2
            _mockTailer.EmitLine("Context After 1"); // Orig 3
            _mockTailer.EmitLine("Context After 2"); // Orig 4
            _mockTailer.EmitLine("Another Line");   // Orig 5
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
            _receivedUpdates.Clear();

            // Act: Update filter with context lines
            _processor.UpdateFilterSettings(new SubstringFilter("MATCH"), 1); // 1 context line
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Past debounce

            // Assert
            Assert.AreEqual(1, _receivedUpdates.Count);
            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Replace, update.Type);
            Assert.AreEqual(3, update.Lines.Count, "Should include match + 1 before + 1 after.");

            // Verify lines and context flag in correct order
            Assert.AreEqual(1, update.Lines[0].OriginalLineNumber, "Line 1 Orig Num");
            Assert.AreEqual("Context Before", update.Lines[0].Text, "Line 1 Text");
            Assert.IsTrue(update.Lines[0].IsContextLine, "Line 1 Context Flag");

            Assert.AreEqual(2, update.Lines[1].OriginalLineNumber, "Line 2 Orig Num");
            Assert.AreEqual("MATCH Line", update.Lines[1].Text, "Line 2 Text");
            Assert.IsFalse(update.Lines[1].IsContextLine, "Line 2 Context Flag"); // The match itself

            Assert.AreEqual(3, update.Lines[2].OriginalLineNumber, "Line 3 Orig Num");
            Assert.AreEqual("Context After 1", update.Lines[2].Text, "Line 3 Text");
            Assert.IsTrue(update.Lines[2].IsContextLine, "Line 3 Context Flag");
        }

        [TestMethod]
        public void TailerError_PropagatesToFilteredUpdatesObserver()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Tailer failed");
            _receivedUpdates.Clear();

            // Act
            _mockTailer.EmitError(expectedException);
            // Errors usually propagate immediately, but advance scheduler slightly just in case
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

            // Assert
            Assert.IsNotNull(_receivedError, "Error should have been received.");
            // Assert.AreSame(expectedException, _receivedError, "Received error should be the one emitted."); // Rx wraps exceptions
            Assert.IsInstanceOfType(_receivedError, typeof(InvalidOperationException));
            Assert.AreEqual(expectedException.Message, _receivedError.Message);
            Assert.AreEqual(0, _receivedUpdates.Count, "No regular updates should be received after error.");
        }

        [TestMethod]
        public void Dispose_StopsProcessingAndCompletesObservable()
        {
            // Arrange
            _receivedUpdates.Clear();
            _mockTailer.EmitLine("Line 1"); // Emit before dispose
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
            Assert.AreEqual(1, _receivedUpdates.Count); // Ensure line was processed
            _receivedUpdates.Clear();

            // Act
            _processor.Dispose();

            // Try emitting after disposal
            _mockTailer.EmitLine("Line 2 (after dispose)");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

            // Assert
            Assert.AreEqual(0, _receivedUpdates.Count, "No updates should be received after Dispose.");
            Assert.IsTrue(_isCompleted, "FilteredUpdates observable should complete on Dispose.");
            // Assert.IsTrue(_mockTailer.IsDisposed); // Processor should *not* dispose injected dependencies
        }
    }
}