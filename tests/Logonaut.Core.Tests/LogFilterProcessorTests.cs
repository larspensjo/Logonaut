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
                new ImmediateSynchronizationContext(),
                _scheduler
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

        [TestMethod] public void Reset_ClearsState_SendsImmediateEmptyUpdate_ThenFiltersAfterInitialRead()
        {
            // Arrange: Add some data and set an initial filter
            _logDocument.AppendLine("Existing Line 1");
            _processor.UpdateFilterSettings(new SubstringFilter("EXISTING"), 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Allow UpdateFilterSettings trigger to fire
            _receivedUpdates.Clear(); // Clear any updates from the initial setup

            // Act: Call Reset
            _processor.Reset();

            // Assert: LogDocument cleared immediately
            Assert.AreEqual(0, _logDocument.Count, "LogDocument should be cleared.");

            // Assert: Immediate empty Replace update was sent via Post
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one immediate empty update.");
            Assert.AreEqual(UpdateType.Replace, _receivedUpdates[0].Type, "Immediate Update type mismatch.");
            Assert.AreEqual(0, _receivedUpdates[0].Lines.Count, "Immediate Update lines count mismatch.");
            _receivedUpdates.Clear(); // Clear this update

            // Act: Emit some lines that *would* match the *new* default filter (TrueFilter) after Reset
            _mockTailer.EmitLine("Line A after Reset");
            _mockTailer.EmitLine("Line B after Reset");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process incremental buffer if needed

            // Assert: NO full filter update yet because InitialReadComplete hasn't fired
            Assert.AreEqual(1, _receivedUpdates.Count, "Should only have incremental Append update before InitialReadComplete."); // Only Append update expected
            Assert.AreEqual(UpdateType.Append, _receivedUpdates[0].Type);
            _receivedUpdates.Clear(); // Clear the append update

            // Act: Simulate the initial read completing
            _mockTailer.SimulateInitialReadComplete();
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Allow the trigger chain to process

            // Assert: NOW the full filter pass runs and sends a Replace update
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Replace update after InitialReadComplete.");
            var finalUpdate = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Replace, finalUpdate.Type, "Final update type mismatch.");
            Assert.AreEqual(2, finalUpdate.Lines.Count, "Final update lines count mismatch.");
            Assert.AreEqual("Line A after Reset", finalUpdate.Lines[0].Text);
            Assert.AreEqual("Line B after Reset", finalUpdate.Lines[1].Text);
        }

        [TestMethod] public void Incremental_AppendsLinesAndAssignsCorrectOriginalNumbers()
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

        [TestMethod] public void UpdateFilterSettings_AfterInitialLoad_TriggersImmediateFullRefilter()
        {
            // Arrange: Simulate initial load completion
            _processor.Reset();
            _mockTailer.EmitLine("Line 1 INFO");
            _mockTailer.EmitLine("Line 2 MATCH");
            _mockTailer.EmitLine("Line 3 INFO");
            _mockTailer.SimulateInitialReadComplete(); // Signal completion
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Process the initial filter pass
            _receivedUpdates.Clear(); // Clear updates from initial load

            // Act: Call UpdateFilterSettings again
            var newFilter = new SubstringFilter("MATCH");
            _processor.UpdateFilterSettings(newFilter, 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Processing should be quick now (no debounce)

            // Assert
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Replace update.");
            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Replace, update.Type);
            Assert.AreEqual(1, update.Lines.Count);
            Assert.AreEqual("Line 2 MATCH", update.Lines[0].Text);
        }

        [TestMethod] public void UpdateFilterSettings_RapidSubsequentCalls_TriggerMultipleRefilters()
        {
            // Arrange: Simulate initial load completion
            _processor.Reset();
            _mockTailer.EmitLine("Line 1 A");
            _mockTailer.EmitLine("Line 2 B");
            _mockTailer.SimulateInitialReadComplete();
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Process initial load
            _receivedUpdates.Clear();

            var filterA = new SubstringFilter("A");
            var filterB = new SubstringFilter("B");

            // Act: Call UpdateFilterSettings rapidly multiple times *after* initial load
            _processor.UpdateFilterSettings(filterA, 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks); // Minimal advance
            _processor.UpdateFilterSettings(filterB, 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
            _processor.UpdateFilterSettings(filterA, 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);

            // Assert: Since there's no debounce on subsequent calls now, each call triggers a filter pass.
            Assert.AreEqual(3, _receivedUpdates.Count, "Should receive three Replace updates.");

            // Check the content of the *last* update (triggered by filterA)
            var lastUpdate = _receivedUpdates[2];
            Assert.AreEqual(UpdateType.Replace, lastUpdate.Type);
            Assert.AreEqual(1, lastUpdate.Lines.Count);
            Assert.AreEqual("Line 1 A", lastUpdate.Lines[0].Text);

            // Optionally check intermediate updates too
            Assert.AreEqual("Line 1 A", _receivedUpdates[0].Lines[0].Text); // First call with filterA
            Assert.AreEqual("Line 2 B", _receivedUpdates[1].Lines[0].Text); // Second call with filterB
        }

        [TestMethod] public void FullRefilter_IncludesContextLines()
        {
            // Arrange: Simulate initial load completion first
            _processor.Reset();
            _mockTailer.EmitLine("Context Before"); // Orig 1
            _mockTailer.EmitLine("MATCH Line");     // Orig 2
            _mockTailer.EmitLine("Context After 1"); // Orig 3
            _mockTailer.EmitLine("Context After 2"); // Orig 4
            _mockTailer.EmitLine("Another Line");   // Orig 5
            _mockTailer.SimulateInitialReadComplete();
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Initial load pass
            _receivedUpdates.Clear();

            // Act: Update filter with context lines
            _processor.UpdateFilterSettings(new SubstringFilter("MATCH"), 1); // 1 context line
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Process update

            // Assert (Assertions remain the same)
            Assert.AreEqual(1, _receivedUpdates.Count);
            var update = _receivedUpdates[0];
            Assert.AreEqual(3, update.Lines.Count);
            Assert.IsTrue(update.Lines[0].IsContextLine);
            Assert.IsFalse(update.Lines[1].IsContextLine); // The match
            Assert.IsTrue(update.Lines[2].IsContextLine);
            Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
            Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
            Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
        }

        [TestMethod] public void FullFilter_WaitsForInitialReadComplete_AfterReset()
        {
            // Arrange
            _processor.Reset();
            _mockTailer.EmitLine("Line 1");
            _mockTailer.EmitLine("Line 2");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process incremental appends if any
            // Clear the immediate empty Reset update and any appends
            _receivedUpdates.Clear();

            // Act: Advance scheduler significantly, but DO NOT signal completion yet
            _scheduler.AdvanceBy(TimeSpan.FromSeconds(10).Ticks);

            // Assert: No full filter update should have occurred
            Assert.AreEqual(0, _receivedUpdates.Count(u => u.Type == UpdateType.Replace), "No Replace update before InitialReadComplete.");

            // Act: Signal completion
            _mockTailer.SimulateInitialReadComplete();
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Allow trigger processing

            // Assert: Now the Replace update should arrive
            Assert.AreEqual(1, _receivedUpdates.Count(u => u.Type == UpdateType.Replace), "Replace update missing after InitialReadComplete.");
            var update = _receivedUpdates.First(u => u.Type == UpdateType.Replace);
            Assert.AreEqual(2, update.Lines.Count); // Should contain Line 1 and Line 2
        }

        [TestMethod] public void FullFilter_HandlesError_DuringInitialReadWait()
        {
            // Arrange
            _processor.Reset();
            _mockTailer.EmitLine("Line 1");
            _receivedUpdates.Clear(); // Clear immediate empty update
            var expectedError = new TimeoutException("Simulated timeout");

            // Act: Simulate an error from the InitialReadComplete observable
            _mockTailer.SimulateInitialReadError(expectedError);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(10).Ticks); // Allow error propagation

            // Assert: The error should propagate to the FilteredUpdates observer
            Assert.IsNotNull(_receivedError, "Error was not received.");
            Assert.IsInstanceOfType(_receivedError, typeof(Exception), "Outer exception type mismatch."); // Processor wraps it
            Assert.IsInstanceOfType(_receivedError.InnerException, typeof(TimeoutException), "Inner exception type mismatch.");
            Assert.AreEqual(expectedError.Message, _receivedError.InnerException.Message);
            Assert.AreEqual(0, _receivedUpdates.Count, "No Replace updates should occur on error.");
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