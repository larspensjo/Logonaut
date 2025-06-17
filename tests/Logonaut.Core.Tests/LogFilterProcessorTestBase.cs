using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency; // For IScheduler
using System.Threading;
using System.Threading.Tasks; // For Task
using Microsoft.Reactive.Testing; // Essential for TestScheduler
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.TestUtils; // Use mocks from here

namespace Logonaut.Core.Tests;

/// <summary>
/// Base class for LogFilterProcessor tests, providing common setup, teardown,
/// mocks, and helper methods.
/// </summary>
[TestClass] public abstract class LogFilterProcessorTestBase // Make abstract
{
    // --- Shared Mocks & Context ---
    // Use protected so derived classes can access them if needed
    protected TestScheduler _backgroundScheduler = null!;
    protected MockLogSource _mockLogSource = null!;
    protected LogDocument _logDocument = null!;
    protected ReactiveFilteredLogStream _filteredStream = null!;
    protected List<FilteredUpdateBase> _receivedUpdates = null!; // Use the base type now
    protected Exception? _receivedError = null;
    protected bool _isCompleted = false;
    protected IDisposable? _subscription;
    protected ImmediateSynchronizationContext _testContext = null!; // Use mock context

    // --- Per-Test Setup ---
    [TestInitialize] public virtual void TestInitialize() // Make virtual if needed, though unlikely
    {
        _backgroundScheduler = new TestScheduler();
        _mockLogSource = new MockLogSource();
        _logDocument = new LogDocument();
        _receivedUpdates = new List<FilteredUpdateBase>(); // Use base type
        _receivedError = null;
        _isCompleted = false;
        _testContext = new ImmediateSynchronizationContext(); // Create mock context

        _filteredStream = new ReactiveFilteredLogStream(
            _mockLogSource,
            _logDocument,
            _testContext, // Use the mock context
            AddLineToLogDocument,
            _backgroundScheduler // Use the TestScheduler
        );

        _subscription = _filteredStream.FilteredUpdates.Subscribe(
            update => _receivedUpdates.Add(update),
            ex => _receivedError = ex,
            () => _isCompleted = true
        );
    }

    // --- Per-Test Teardown ---
    [TestCleanup] public virtual void TestCleanup() // Make virtual if needed
    {
        _subscription?.Dispose();
        _filteredStream?.Dispose(); // Dispose processor first
        _mockLogSource?.Dispose(); // Then dispose the source mock
    }

    // --- Helper Methods ---

    /// <summary>
    /// Callback passed to LogFilterProcessor to add lines to the shared LogDocument.
    /// </summary>
    protected void AddLineToLogDocument(string line)
    {
        _logDocument.AppendLine(line);
    }

    /// <summary>
    /// Sets up the mock source and processor for an initial load scenario.
    /// Clears the received updates list AFTER setup is complete.
    /// </summary>
    protected async Task SetupInitialFileLoad(List<string> initialLines, IFilter? initialFilter = null, int context = 0)
    {
        _filteredStream.Reset(); // Reset processor state

        _mockLogSource.LinesForInitialRead = initialLines; // Set lines for mock source

        _logDocument.Clear(); // Clear document before Prepare call
        long linesRead = await _mockLogSource.PrepareAndGetInitialLinesAsync("C:\\test.log", AddLineToLogDocument);
        Assert.AreEqual(initialLines.Count, linesRead, "MockLogSource did not report correct lines read.");
        Assert.AreEqual(initialLines.Count, _logDocument.Count, "LogDocument not populated correctly by Prepare callback.");

        _mockLogSource.Start(); // Start source monitoring
        Assert.IsTrue(_mockLogSource.IsRunning, "MockLogSource should be monitoring after StartMonitoring.");

        // Trigger the *first* filter application after preparation
        _filteredStream.UpdateFilterSettings(initialFilter ?? new TrueFilter(), context);

        // Advance scheduler past throttle/debounce time FOR THE INITIAL LOAD
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Adjust time if needed

        // Clear updates received *during this setup* so tests assert on subsequent updates
        _receivedUpdates.Clear();
    }

    /// <summary>
    /// Helper to extract only the text content from a list of FilteredLogLine.
    /// </summary>
    protected static List<string> GetLinesText(FilteredUpdateBase update)
    {
        return update.Lines.Select(l => l.Text).ToList();
    }

    // NOTE: Do NOT put the 'FalseFilter' class here. Keep specific test helpers
    //       within the test files that use them, or in a separate shared test utilities file
    //       if used across many different test classes.
}
