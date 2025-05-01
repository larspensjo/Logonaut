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
/// Tests focused on the lifecycle (Dispose) and error handling behavior
/// of the LogFilterProcessor.
/// </summary>
[TestClass] public class LogFilterProcessor_LifecycleErrorTests : LogFilterProcessorTestBase
{
    // Verifies: [ReqFileUnavailableHandlingv1] (Error propagation)
    [TestMethod] public async Task LogSourceError_PropagatesToFilteredUpdatesObserver()
    {
        // Arrange: Setup initial load successfully
        await SetupInitialFileLoad(new List<string> { "Line 1" });
        var expectedException = new InvalidOperationException("Log source failed");

        // Act: Emit error *after* initial load
        _mockLogSource.EmitError(expectedException); // Use MockLogSource method
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks); // Allow error to propagate

        // Assert
        Assert.IsNotNull(_receivedError, "Error should have been received.");
        // Assert.IsInstanceOfType(_receivedError, typeof(InvalidOperationException)); // More specific check
        Assert.IsTrue(_receivedError is InvalidOperationException || _receivedError?.InnerException is InvalidOperationException, "Unexpected error type");
        Assert.IsTrue(_receivedError!.Message.Contains("Log source failed") || _receivedError!.InnerException!.Message.Contains("Log source failed"), "Error message mismatch");

        int updateCountBeforeError = _receivedUpdates.Count;

        // Act: Try emitting after error
        _mockLogSource.EmitLine("After Error"); // Use MockLogSource method
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

        // Assert: No more updates after error
        Assert.AreEqual(updateCountBeforeError, _receivedUpdates.Count);
    }

    // Verifies: Internal resource cleanup
    [TestMethod] public async Task Dispose_StopsProcessingAndCompletesObservable()
    {
        // Arrange: Setup initial load and process an emit
        await SetupInitialFileLoad(new List<string> { "Initial" });
        _mockLogSource.EmitLine("Append 1");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow emit to be processed
        Assert.AreEqual(1, _receivedUpdates.Count, "Arrange failure: Emit was not processed.");
        _receivedUpdates.Clear(); // Clear updates before disposal

        // Act
        _processor.Dispose(); // Dispose the processor

        // Try emitting after processor disposal.
        _mockLogSource.EmitLine("Append 2 (after processor dispose)"); // Call is valid on mock source

        // Advance scheduler to see if any processing happens (it shouldn't)
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

        // Assert
        Assert.AreEqual(0, _receivedUpdates.Count, "No updates should be received after Processor Dispose.");
        Assert.IsTrue(_isCompleted, "FilteredUpdates observable should complete on Processor Dispose.");
        Assert.IsFalse(_mockLogSource.IsDisposed, "Processor should NOT dispose the injected log source.");
    }

#pragma warning disable CS1998 // The warning is technically accurate but harmless in this test context.
    // Verifies: Internal error handling during full re-filter
    [TestMethod] public async Task Processor_ErrorInFullFilter_PropagatesAndStops()
    {
        // Arrange: Setup initial load
        await SetupInitialFileLoad(new List<string> { "Line 1" });
        var expectedException = new InvalidOperationException("Error during ApplyFilterToAll simulation");

        // Act: Trigger a full refilter with the throwing filter
        _processor.UpdateFilterSettings(new ThrowingFilter(), 0);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow throttle and Select to execute

        // Assert
        Assert.IsNotNull(_receivedError, "Error should have been received from filter update.");
        Assert.IsInstanceOfType(_receivedError, typeof(InvalidOperationException));
        Assert.AreEqual("Simulated filter error", _receivedError.Message);

        int updateCountBeforeError = _receivedUpdates.Count;

        // Act: Try emitting after error
        _mockLogSource.EmitLine("After Error");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);

        // Assert: No more updates after error
        Assert.AreEqual(updateCountBeforeError, _receivedUpdates.Count);
    }
#pragma warning restore CS0162 // Restore unreachable code warning

    // Verifies: Internal error handling during incremental filtering (after Step 4)
    [TestMethod] [Ignore] public async Task Processor_ErrorInIncrementalFilter_PropagatesAndStops()
    {
        // Arrange: Setup initial load
        await SetupInitialFileLoad(new List<string> { "Line 1" });
        var expectedException = new InvalidOperationException("Error during ApplyFilterToSubset simulation");

        // Arrange: Need a way to make ApplyFilterToSubset throw. Could use a special filter
        // or modify the test setup if FilterEngine was injectable. Let's use a throwing filter.
        _processor.UpdateFilterSettings(new ThrowingFilter(), 0); // Use the same filter
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Process this setting change
        _receivedUpdates.Clear(); // Clear the error from the settings change itself
        _receivedError = null;

        // Act: Emit a line that triggers the *incremental* path (after Step 4)
        _mockLogSource.EmitLine("Trigger Incremental");
         // Advance scheduler enough for the incremental pipeline (including buffer)
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsNotNull(_receivedError, "Error should have been received from incremental update.");
        Assert.IsInstanceOfType(_receivedError, typeof(InvalidOperationException));
        Assert.AreEqual("Simulated filter error", _receivedError.Message);

        int updateCountBeforeError = _receivedUpdates.Count;
        // Try emitting after error
        _mockLogSource.EmitLine("After Error 2");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(150).Ticks);
        Assert.AreEqual(updateCountBeforeError, _receivedUpdates.Count); // No more updates
    }

    // Helper needed for error test
    private class ThrowingFilter : IFilter
    {
        public bool Enabled { get; set; } = true;
        public bool IsEditable => false;
        public string DisplayText => "THROW";
        public string TypeText => "Throwing";
        public string Value { get; set; } = "";
        public bool IsMatch(string line) => throw new InvalidOperationException("Simulated filter error");
    }
}
