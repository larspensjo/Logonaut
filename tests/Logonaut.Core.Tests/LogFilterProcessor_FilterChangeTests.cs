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
/// Tests focused on how the LogFilterProcessor handles changes to
/// filter settings via the UpdateFilterSettings method.
/// </summary>
[TestClass] public class LogFilterProcessor_FilterChangeTests : LogFilterProcessorTestBase
{
    // Verifies: [ReqFilterDynamicUpdateViewv1], [ReqFilterEfficientRealTimev1] (Full path for settings)
    [TestMethod] public async Task UpdateFilterSettings_TriggersFullRefilter_OnCurrentDocumentContent()
    {
        // Arrange: Setup initial load with TrueFilter
        var initialLines = new List<string> { "Line 1 INFO", "Line 2 MATCH", "Line 3 INFO" };
        await SetupInitialFileLoad(initialLines); // Clears updates after setup

        // Act: Update filter settings to something specific
        var newFilter = new SubstringFilter("MATCH");
        _filteredStream.UpdateFilterSettings(newFilter, 0);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle

        // Assert: Should receive one *new* Replace update
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(1, update.Lines.Count);
        Assert.AreEqual("Line 2 MATCH", update.Lines[0].Text);
        Assert.AreEqual(2, update.Lines[0].OriginalLineNumber);
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Throttle on settings changes)
    [TestMethod] public async Task UpdateFilterSettings_RapidSubsequentCalls_ThrottledToOneUpdate()
    {
        // Arrange: Setup initial load
        await SetupInitialFileLoad(new List<string> { "Line 1 A", "Line 2 B" });

        var filterA = new SubstringFilter("A");
        var filterB = new SubstringFilter("B");

        // Act: Call UpdateFilterSettings rapidly
        _filteredStream.UpdateFilterSettings(filterA, 0);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
        _filteredStream.UpdateFilterSettings(new TrueFilter(), 0);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);
        _filteredStream.UpdateFilterSettings(filterB, 0); // Final filter
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(5).Ticks);

        // Assert: No updates yet
        Assert.AreEqual(0, _receivedUpdates.Count);

        // Act: Advance time *past* the throttle window
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);

        // Assert: Only ONE update received, using the LAST filter settings (filterB)
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(1, update.Lines.Count); // Filter B matches "Line 2 B"
        Assert.AreEqual("Line 2 B", update.Lines[0].Text);
        Assert.AreEqual(2, update.Lines[0].OriginalLineNumber);
    }

    // Verifies: [ReqFilterContextLinesv1] (Applied during full re-filter)
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
        _filteredStream.UpdateFilterSettings(new SubstringFilter("MATCH"), 1); // 1 context line
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow Throttle

        // Assert
        Assert.AreEqual(1, _receivedUpdates.Count);
        Assert.IsInstanceOfType(_receivedUpdates[0], typeof(ReplaceFilteredUpdate));
        var update = (ReplaceFilteredUpdate)_receivedUpdates[0];
        Assert.AreEqual(3, update.Lines.Count);

        var lines = update.Lines; // Alias for readability
        Assert.AreEqual("Context Before", lines[0].Text);
        Assert.IsTrue(lines[0].IsContextLine);
        Assert.AreEqual(1, lines[0].OriginalLineNumber);

        Assert.AreEqual("MATCH Line", lines[1].Text);
        Assert.IsFalse(lines[1].IsContextLine);
        Assert.AreEqual(2, lines[1].OriginalLineNumber);

        Assert.AreEqual("Context After 1", lines[2].Text);
        Assert.IsTrue(lines[2].IsContextLine);
        Assert.AreEqual(3, lines[2].OriginalLineNumber);
    }
}
