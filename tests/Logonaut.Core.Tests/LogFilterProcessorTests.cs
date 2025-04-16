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

namespace Logonaut.Core.Tests
{

    // TODO: Should be made common with the one in MainViewModelTests.cs
    public class MockLogTailerService : ILogTailerService
    {
        private readonly Subject<string> _logLinesSubject = new Subject<string>();
        public string? ChangedFilePath { get; private set; }
        public bool IsDisposed { get; private set; } = false;
        public bool IsStopped { get; private set; } = false;
        public Exception? LastErrorReceived { get; private set; } // Track errors

        public IObservable<string> LogLines => _logLinesSubject.AsObservable();

        public void ChangeFile(string filePath)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(MockLogTailerService));
            ChangedFilePath = filePath;
            IsStopped = false;
        }

        public void StopTailing()
        {
            IsStopped = true;
        }

        // Simulate emitting a log line
        public void EmitLine(string line)
        {
            if (IsDisposed || IsStopped) return;
            _logLinesSubject.OnNext(line);
        }

        // Simulate emitting an error
        public void EmitError(Exception ex)
        {
            if (IsDisposed || IsStopped) return;
            LastErrorReceived = ex; // Store the error
            _logLinesSubject.OnError(ex);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            _logLinesSubject.OnCompleted();
            _logLinesSubject.Dispose();
        }
    }

    // Logonaut.Core.Tests/LogFilterProcessorTests.cs
    [TestClass]
    public class LogFilterProcessorTests
    {
        private TestScheduler _scheduler = null!; // For controlling virtual time
        private MockLogTailerService _mockTailer = null!;
        private LogDocument _logDocument = null!;
        private LogFilterProcessor _processor = null!;
        private List<FilteredUpdate> _receivedUpdates = null!; // Capture results
        private Exception? _receivedError = null; // Capture errors
        private IDisposable? _subscription;

        [TestInitialize]
        public void TestInitialize()
        {
            _scheduler = new TestScheduler();
            _mockTailer = new MockLogTailerService();
            _logDocument = new LogDocument();
            _receivedUpdates = new List<FilteredUpdate>();
            _receivedError = null;

            // Use a simple SyncContext for tests. Testing the actual UI marshalling
            // is complex; we focus on the logic *before* ObserveOn(_uiContext).
            // The IScheduler parameter in the constructor is key for testing time.
            _processor = new LogFilterProcessor(
                _mockTailer,
                _logDocument,
                new SynchronizationContext(), // Basic context for test execution
                _scheduler // Inject the TestScheduler!
            );

            // Subscribe to capture results and errors
            _subscription = _processor.FilteredUpdates.Subscribe(
                update => _receivedUpdates.Add(update),
                ex => _receivedError = ex
            );
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _subscription?.Dispose();
            _processor?.Dispose();
            _mockTailer?.Dispose(); // Dispose the mock if needed
        }

        [TestMethod]
        public void Reset_ClearsDocumentAndEmitsEmptyReplaceUpdate()
        {
            // Arrange
            _logDocument.AppendLine("Existing Line 1");
            _logDocument.AppendLine("Existing Line 2");
            _processor.UpdateFilterSettings(new SubstringFilter("test"), 1); // Set non-default state
            _receivedUpdates.Clear(); // Clear any initial updates

            // Act
            _processor.Reset();
            // Reset should bypass scheduler for the immediate empty update push via uiContext.Post
            // but let's advance time slightly just in case any scheduled actions might interfere (unlikely for Reset)
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);

            // Assert
            Assert.AreEqual(0, _logDocument.Count, "LogDocument should be cleared.");
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one update after Reset.");

            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Replace, update.Type, "Update type should be Replace.");
            Assert.AreEqual(0, update.Lines.Count, "Update should contain zero lines.");

            // Also check if subsequent filter trigger uses default state (tested implicitly elsewhere)
        }

        [TestMethod]
        public void Incremental_AppendsLinesAndAssignsCorrectOriginalNumbers()
        {
            // Arrange
            _processor.UpdateFilterSettings(new TrueFilter(), 0); // Match everything initially
            _receivedUpdates.Clear();

            // Act
            _mockTailer.EmitLine("Line A");
            _mockTailer.EmitLine("Line B");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Advance past buffer time

            _mockTailer.EmitLine("Line C");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Advance past buffer time

            // Assert
            Assert.AreEqual(3, _logDocument.Count, "LogDocument should have 3 lines.");
            Assert.AreEqual("Line A", _logDocument[0]);
            Assert.AreEqual("Line B", _logDocument[1]);
            Assert.AreEqual("Line C", _logDocument[2]);

            // Expecting two Append updates (one for A/B, one for C)
            Assert.AreEqual(2, _receivedUpdates.Count, "Should have received two updates.");

            // Check first update (Lines A & B)
            var update1 = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update1.Type);
            Assert.AreEqual(2, update1.Lines.Count);
            Assert.AreEqual(1, update1.Lines[0].OriginalLineNumber);
            Assert.AreEqual("Line A", update1.Lines[0].Text);
            Assert.AreEqual(2, update1.Lines[1].OriginalLineNumber);
            Assert.AreEqual("Line B", update1.Lines[1].Text);

            // Check second update (Line C)
            var update2 = _receivedUpdates[1];
            Assert.AreEqual(UpdateType.Append, update2.Type);
            Assert.AreEqual(1, update2.Lines.Count);
            Assert.AreEqual(3, update2.Lines[0].OriginalLineNumber);
            Assert.AreEqual("Line C", update2.Lines[0].Text);
        }

        [TestMethod]
        public void Incremental_AppliesCurrentFilterToBufferedLines()
        {
            // Arrange
            _processor.UpdateFilterSettings(new SubstringFilter("MATCH"), 0);
            _receivedUpdates.Clear();

            // Act
            _mockTailer.EmitLine("IGNORE 1");
            _mockTailer.EmitLine("MATCH 2");
            _mockTailer.EmitLine("IGNORE 3");
            _mockTailer.EmitLine("MATCH 4");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process buffer

            // Assert
            Assert.AreEqual(4, _logDocument.Count, "LogDocument should contain all lines.");
            Assert.AreEqual(1, _receivedUpdates.Count, "Should receive one update.");

            var update = _receivedUpdates[0];
            Assert.AreEqual(UpdateType.Append, update.Type);
            Assert.AreEqual(2, update.Lines.Count, "Update should contain only matching lines.");
            Assert.AreEqual(2, update.Lines[0].OriginalLineNumber); // Original line number of "MATCH 2"
            Assert.AreEqual("MATCH 2", update.Lines[0].Text);
            Assert.AreEqual(4, update.Lines[1].OriginalLineNumber); // Original line number of "MATCH 4"
            Assert.AreEqual("MATCH 4", update.Lines[1].Text);
        }


        [TestMethod]
        public void UpdateFilterSettings_TriggersFullRefilterWithLatestSettings()
        {
            // Arrange: Add some initial lines and process them
            _processor.UpdateFilterSettings(new TrueFilter(), 0);
            _mockTailer.EmitLine("Line 1 INFO");
            _mockTailer.EmitLine("Line 2 MATCH");
            _mockTailer.EmitLine("Line 3 INFO");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks); // Process initial lines (results ignored for this test focus)
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
            _mockTailer.EmitLine("Line 1 A");
            _mockTailer.EmitLine("Line 2 B");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
            _receivedUpdates.Clear();

            var filterA = new SubstringFilter("A");
            var filterB = new SubstringFilter("B");

            // Act: Call UpdateFilterSettings rapidly
            _processor.UpdateFilterSettings(filterA, 0); // Time: 0ms (virtual)
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // Time: 100ms
            _processor.UpdateFilterSettings(filterB, 0); // Time: 100ms
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // Time: 200ms

            // Assert: No update yet, debounce timer restarted at 100ms
            Assert.AreEqual(0, _receivedUpdates.Count, "No update should occur before debounce period expires.");

            // Act: Advance past the debounce period started at 100ms (100 + 300 = 400ms)
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(250).Ticks); // Total Time: 450ms

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

            // Verify lines and context flag
            Assert.AreEqual("Context Before", update.Lines[0].Text);
            Assert.AreEqual(1, update.Lines[0].OriginalLineNumber);
            Assert.IsTrue(update.Lines[0].IsContextLine);

            Assert.AreEqual("MATCH Line", update.Lines[1].Text);
            Assert.AreEqual(2, update.Lines[1].OriginalLineNumber);
            Assert.IsFalse(update.Lines[1].IsContextLine); // The match itself

            Assert.AreEqual("Context After 1", update.Lines[2].Text);
            Assert.AreEqual(3, update.Lines[2].OriginalLineNumber);
            Assert.IsTrue(update.Lines[2].IsContextLine);
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
            Assert.AreSame(expectedException, _receivedError, "Received error should be the one emitted.");
            Assert.AreEqual(0, _receivedUpdates.Count, "No regular updates should be received after error.");
        }

        [TestMethod]
        public void Dispose_StopsProcessingAndCompletesObservable()
        {
            // Arrange
            bool isCompleted = false;
            _processor.FilteredUpdates.Subscribe(_ => { }, () => isCompleted = true); // Subscribe to completion

            _mockTailer.EmitLine("Line 1");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
            _receivedUpdates.Clear();

            // Act
            _processor.Dispose();
            // Try emitting after disposal
            _mockTailer.EmitLine("Line 2 (after dispose)");
            _scheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

            // Assert
            Assert.AreEqual(0, _receivedUpdates.Count, "No updates should be received after Dispose.");
            Assert.IsTrue(isCompleted, "FilteredUpdates observable should complete on Dispose.");
            // Assert.IsTrue(_mockTailer.IsDisposed); // Processor should *not* dispose injected dependencies
        }
    }
}