// ===== File: tests\Logonaut.Core.Tests\FilterEngine_ApplyFilterToSubsetTests.cs =====

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Core; // For OriginalLineInfo and FilterEngine
using Logonaut.Filters;

namespace Logonaut.Core.Tests;

[TestClass] public class FilterEngine_ApplyFilterToSubsetTests
{
    private LogDocument _logDoc = null!;

    // A neutral filter that always returns true.
    public class FalseFilter : FilterBase
    {
        public override bool IsMatch(string line) => false;

        public override string DisplayText => "FALSE";

        public override string TypeText => "FALSE";
    }

    [TestInitialize] public void TestInitialize()
    {
        _logDoc = new LogDocument();
        // Add common baseline data used by multiple tests
        _logDoc.AppendLine("Line 1: Start");          // Index 0, Num 1
        _logDoc.AppendLine("Line 2: Context Before"); // Index 1, Num 2
        _logDoc.AppendLine("Line 3: MATCH_A");        // Index 2, Num 3
        _logDoc.AppendLine("Line 4: Context After");  // Index 3, Num 4
        _logDoc.AppendLine("Line 5: Middle");         // Index 4, Num 5
        _logDoc.AppendLine("Line 6: Context Before2");// Index 5, Num 6
        _logDoc.AppendLine("Line 7: MATCH_B");        // Index 6, Num 7
        _logDoc.AppendLine("Line 8: End Context");    // Index 7, Num 8
        _logDoc.AppendLine("Line 9: Final");          // Index 8, Num 9
    }

    // Helper to simplify assertions
    private void AssertLine(FilteredLogLine actual, int expectedNum, string expectedText, bool expectedContext)
    {
        Assert.AreEqual(expectedNum, actual.OriginalLineNumber, $"LineNum mismatch for '{expectedText}'");
        Assert.AreEqual(expectedText, actual.Text, "Text mismatch");
        Assert.AreEqual(expectedContext, actual.IsContextLine, $"IsContext mismatch for '{expectedText}'");
    }

    // Verifies: [ReqFilterEfficientRealTimev1] (Core Logic), [ReqFilterDisplayMatchingLinesv1]
    [TestMethod] public void ApplyFilterToSubset_NoMatches_ReturnsEmptyList()
    {
        // Arrange
        var newLines = new List<OriginalLineInfo> { new(4, "Line 5: Middle") };
        var filter = new SubstringFilter("NOMATCH");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1);

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterDisplayMatchingLinesv1]
    [TestMethod] public void ApplyFilterToSubset_SingleMatchNoContext_ReturnsOnlyMatch()
    {
        // Arrange
        var newLines = new List<OriginalLineInfo> { new(2, "Line 3: MATCH_A") }; // Processing line 3
        var filter = new SubstringFilter("MATCH_A");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 0); // Context = 0

        // Assert
        Assert.AreEqual(1, result.Count);
        AssertLine(result[0], 3, "Line 3: MATCH_A", false);
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterContextLinesv1]
    [TestMethod] public void ApplyFilterToSubset_SingleMatchWithContext_ReturnsMatchAndContext()
    {
        // Arrange
        var newLines = new List<OriginalLineInfo> { new(2, "Line 3: MATCH_A") }; // Processing line 3
        var filter = new SubstringFilter("MATCH_A");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1); // Context = 1

        // Assert: Expect Lines 2, 3, 4
        Assert.AreEqual(3, result.Count);
        AssertLine(result[0], 2, "Line 2: Context Before", true);
        AssertLine(result[1], 3, "Line 3: MATCH_A", false);
        AssertLine(result[2], 4, "Line 4: Context After", true);
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterContextLinesv1] (Boundary Start)
    [TestMethod] public void ApplyFilterToSubset_MatchAtStartWithContext_ReturnsCorrectContext()
    {
        // Arrange
        var newLines = new List<OriginalLineInfo> { new(0, "Line 1: Start") }; // Processing line 1
        var filter = new SubstringFilter("Start"); // Match line 1

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 2); // Context = 2

        // Assert: Expect Lines 1, 2, 3
        Assert.AreEqual(3, result.Count);
        AssertLine(result[0], 1, "Line 1: Start", false);
        AssertLine(result[1], 2, "Line 2: Context Before", true);
        AssertLine(result[2], 3, "Line 3: MATCH_A", true);
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterContextLinesv1] (Boundary End)
    [TestMethod] public void ApplyFilterToSubset_MatchAtEndWithContext_ReturnsCorrectContext()
    {
        // Arrange
        var newLines = new List<OriginalLineInfo> { new(8, "Line 9: Final") }; // Processing line 9
        var filter = new SubstringFilter("Final"); // Match line 9

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 2); // Context = 2

        // Assert: Expect Lines 7, 8, 9
        Assert.AreEqual(3, result.Count);
        AssertLine(result[0], 7, "Line 7: MATCH_B", true);
        AssertLine(result[1], 8, "Line 8: End Context", true);
        AssertLine(result[2], 9, "Line 9: Final", false);
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterContextLinesv1] (Multiple Matches)
    [TestMethod] public void ApplyFilterToSubset_MultipleMatchesInBatch_ReturnsAllMatchesAndContext_OrderedUnique()
    {
        // Arrange
        // Processing lines 3 and 7 in the same batch
        var newLines = new List<OriginalLineInfo> {
            new(2, "Line 3: MATCH_A"),
            new(6, "Line 7: MATCH_B")
        };
        var filter = new SubstringFilter("MATCH");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1); // Context = 1

        // Assert: Expect unique lines 2, 3, 4 (from A), 6, 7, 8 (from B)
        Assert.AreEqual(6, result.Count);
        AssertLine(result[0], 2, "Line 2: Context Before", true);    // Context for A
        AssertLine(result[1], 3, "Line 3: MATCH_A", false);           // Match A
        AssertLine(result[2], 4, "Line 4: Context After", true);     // Context for A
        AssertLine(result[3], 6, "Line 6: Context Before2", true);   // Context for B
        AssertLine(result[4], 7, "Line 7: MATCH_B", false);           // Match B
        AssertLine(result[5], 8, "Line 8: End Context", true);     // Context for B
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterContextLinesv1] (Context Overlap)
    [TestMethod] public void ApplyFilterToSubset_OverlappingContext_ReturnsUniqueLines()
    {
        // Arrange: Add a closer match to cause context overlap
        _logDoc = new LogDocument();
        _logDoc.AppendLine("Line 1"); // Index 0, Num 1
        _logDoc.AppendLine("Line 2 MATCH"); // Index 1, Num 2
        _logDoc.AppendLine("Line 3 Context"); // Index 2, Num 3
        _logDoc.AppendLine("Line 4 MATCH"); // Index 3, Num 4
        _logDoc.AppendLine("Line 5"); // Index 4, Num 5

        // Processing lines 2 and 4
        var newLines = new List<OriginalLineInfo> { new(1, "Line 2 MATCH"), new(3, "Line 4 MATCH") };
        var filter = new SubstringFilter("MATCH");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1); // Context = 1

        // Assert: Expect unique lines 1, 2(M), 3(C), 4(M), 5
        // Line 3 is context for both Line 2 and Line 4, should appear once.
        Assert.AreEqual(5, result.Count);
        AssertLine(result[0], 1, "Line 1", true);         // Context for 2
        AssertLine(result[1], 2, "Line 2 MATCH", false);  // Match 2
        AssertLine(result[2], 3, "Line 3 Context", true);  // Context for 2 and 4
        AssertLine(result[3], 4, "Line 4 MATCH", false);  // Match 4
        AssertLine(result[4], 5, "Line 5", true);         // Context for 4
    }

    // Verifies: [ReqFilterEfficientRealTimev1], [ReqFilterContextLinesv1] (Match vs Context Priority)
    [TestMethod] public void ApplyFilterToSubset_LineIsMatchAndContext_MarkedAsMatch()
    {
        // Arrange: Line 3 is match, Line 4 is context for 3, Line 4 is also a match later
        _logDoc = new LogDocument();
        _logDoc.AppendLine("Line 1"); // Index 0, Num 1
        _logDoc.AppendLine("Line 2 Context"); // Index 1, Num 2
        _logDoc.AppendLine("Line 3 MATCH"); // Index 2, Num 3
        _logDoc.AppendLine("Line 4 MATCH"); // Index 3, Num 4
        _logDoc.AppendLine("Line 5 Context"); // Index 4, Num 5

        // Processing lines 3 and 4
        var newLines = new List<OriginalLineInfo> { new(2, "Line 3 MATCH"), new(3, "Line 4 MATCH") };
        var filter = new SubstringFilter("MATCH");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1); // Context = 1

        // Assert: Expect lines 2(C), 3(M), 4(M), 5(C)
        // Line 4 should be marked as a match (IsContextLine=false) even though it's context for line 3.
        Assert.AreEqual(4, result.Count);
        AssertLine(result[0], 2, "Line 2 Context", true);
        AssertLine(result[1], 3, "Line 3 MATCH", false);
        AssertLine(result[2], 4, "Line 4 MATCH", false); // <<< Should be false (it's a match)
        AssertLine(result[3], 5, "Line 5 Context", true);
    }

    // Verifies internal logic (robustness)
    [TestMethod] public void ApplyFilterToSubset_NewLineIndexOutOfBounds_SkipsLineGracefully()
    {
        // Arrange
        var newLines = new List<OriginalLineInfo> {
            new(2, "Line 3: MATCH_A"),
            new(100, "Line 101: FAKE MATCH") // Index 100 is out of bounds for _logDoc (size 9)
        };
        var filter = new SubstringFilter("MATCH");

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1);

        // Assert: Only lines related to MATCH_A (index 2) should be returned. Line 101 is skipped.
        Assert.AreEqual(3, result.Count);
        AssertLine(result[0], 2, "Line 2: Context Before", true);
        AssertLine(result[1], 3, "Line 3: MATCH_A", false);
        AssertLine(result[2], 4, "Line 4: Context After", true);
    }

        // Verifies: [ReqFilterEfficientRealTimev1] (Handles FalseFilter correctly)
    [TestMethod] public void ApplyFilterToSubset_FalseFilter_ReturnsEmpty()
    {
            // Arrange
        var newLines = new List<OriginalLineInfo> {
            new(2, "Line 3: MATCH_A"),
            new(6, "Line 7: MATCH_B")
        };
        var filter = new FalseFilter(); // Use the test helper filter

        // Act
        var result = FilterEngine.ApplyFilterToSubset(newLines, _logDoc, filter, 1);

        // Assert
        Assert.AreEqual(0, result.Count);
    }
}
