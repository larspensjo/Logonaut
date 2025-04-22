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
using Logonaut.TestUtils; // Use mocks from here

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

        private void SetupInitialFileLoad(List<string> initialLines, IFilter? initialFilter = null, int context = 0)
        {
            _processor.Reset();
            _mockTailer.LinesForInitialRead = initialLines;
            var task = _mockTailer.ChangeFileAsync("C:\\test.log", _logDocument); task.Wait();
            Assert.AreEqual(initialLines.Count, _logDocument.Count, "LogDocument not populated correctly by mock ChangeFileAsync.");
            _processor.UpdateFilterSettings(initialFilter ?? new TrueFilter(), context);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Advance past throttle
            Assert.IsTrue(_receivedUpdates.Any(u => u.Type == UpdateType.Replace), "Initial Replace update missing after setup.");
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


        [TestMethod] public void Incremental_AppendsLinesReceivedAFTERInitialLoad()
        {
            // Arrange: Use setup helper
            SetupInitialFileLoad(new List<string> { "Initial A", "Initial B" });

            // Act: Emit lines *after* initial load setup
            _mockTailer.EmitLine("Line C"); // Processor index 1 (since reset)
            _mockTailer.EmitLine("Line D"); // Processor index 2
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process buffer

            // Assert Document Content (Stays as initial)
            Assert.AreEqual(2, _logDocument.Count, "LogDocument count should match initial lines.");
            Assert.AreEqual("Initial A", _logDocument[0]);
            Assert.AreEqual("Initial B", _logDocument[1]);

            // Assert Received Updates (Append update expected)
            Assert.AreEqual(1, _receivedUpdates.Count, "Should have received one Append update.");
            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update.Type);
            Assert.AreEqual(2, update.Lines.Count);
            Assert.AreEqual(1, update.Lines[0].OriginalLineNumber, "Append line 1 original number mismatch.");
            Assert.AreEqual("Line C", update.Lines[0].Text);
            Assert.AreEqual(2, update.Lines[1].OriginalLineNumber, "Append line 2 original number mismatch.");
            Assert.AreEqual("Line D", update.Lines[1].Text);
        }

        [TestMethod] public void Incremental_FiltersAppendedLinesCorrectly()
        {
            // Arrange: Setup initial load
            var initialLines = new List<string> { "Initial Keep" };
            SetupInitialFileLoad(initialLines); // Uses TrueFilter initially

            // Arrange: Set specific filter *after* initial load
            _processor.UpdateFilterSettings(new SubstringFilter("MATCH"), 0);
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle
            _receivedUpdates.Clear(); // Clear the filter update

            // Act: Emit lines
            _mockTailer.EmitLine("IGNORE 1");
            _mockTailer.EmitLine("MATCH 2");
            _mockTailer.EmitLine("IGNORE 3");
            _mockTailer.EmitLine("MATCH 4");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process buffer

            // Assert Document Content
            Assert.AreEqual(initialLines.Count, _logDocument.Count, "LogDocument count should match initial lines.");
            Assert.AreEqual("Initial Keep", _logDocument[0]);

            // Assert Received Updates
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one Append update.");
            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update.Type);
            Assert.AreEqual(2, update.Lines.Count); // Only matching lines
            Assert.AreEqual(2, update.Lines[0].OriginalLineNumber); // Processor index 2
            Assert.AreEqual("MATCH 2", update.Lines[0].Text);
            Assert.AreEqual(4, update.Lines[1].OriginalLineNumber); // Processor index 4
            Assert.AreEqual("MATCH 4", update.Lines[1].Text);
        }

        [TestMethod] public void Incremental_OriginalLineNumbersAreRelativeToReset()
        {
            // Arrange: Perform initial load
            SetupInitialFileLoad(new List<string> { "Line 1", "Line 2", "Line 3" });

            // Act: Emit new lines
            _mockTailer.EmitLine("Line A"); // Should get OriginalLineNumber 1 (relative to reset)
            _mockTailer.EmitLine("Line B"); // Should get OriginalLineNumber 2
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

            _mockTailer.EmitLine("Line C"); // Should get OriginalLineNumber 3
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);


            // Assert: Check the Append updates
            Assert.AreEqual(2, _receivedUpdates.Count, "Should have received two Append updates.");

            // First Append (A, B)
            var update1 = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update1.Type);
            Assert.AreEqual(2, update1.Lines.Count);
            Assert.AreEqual(1, update1.Lines[0].OriginalLineNumber); // Relative index 1
            Assert.AreEqual("Line A", update1.Lines[0].Text);
            Assert.AreEqual(2, update1.Lines[1].OriginalLineNumber); // Relative index 2
            Assert.AreEqual("Line B", update1.Lines[1].Text);

            // Second Append (C)
            var update2 = _receivedUpdates[1];
            Assert.AreEqual(UpdateType.Append, update2.Type);
            Assert.AreEqual(1, update2.Lines.Count);
            Assert.AreEqual(3, update2.Lines[0].OriginalLineNumber); // Relative index 3
            Assert.AreEqual("Line C", update2.Lines[0].Text);
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
            Assert.AreEqual(UpdateType.Replace, update.Type);
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
            Assert.AreEqual(UpdateType.Replace, update.Type);
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
            Assert.AreEqual(UpdateType.Replace, update.Type);
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
             _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
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
    }
}
