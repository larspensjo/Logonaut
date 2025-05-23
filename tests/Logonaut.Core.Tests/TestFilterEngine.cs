using Logonaut.Common;
using Logonaut.Filters;

namespace Logonaut.Core.Tests
{
    [TestClass]
    public class FilterEngineTests
    {
        private static List<string> GetTextLines(IReadOnlyList<FilteredLogLine> lines)
        {
            return lines.Select(line => line.Text).ToList();
        }

        [TestMethod]
        public void ApplyFilters_WithTrueFilterAndZeroContext_ReturnsAllLines()
        {
            // Arrange
            var doc = new LogDocument();
            doc.AppendLine("Line 1");
            doc.AppendLine("Line 2");
            doc.AppendLine("Line 3");

            // Act
            var result = FilterEngine.ApplyFilters(doc, new TrueFilter(), contextLines: 0);

            // Assert: All lines should be returned exactly once.
            Assert.AreEqual(3, result.Count);
            CollectionAssert.AreEqual(new List<string> { "Line 1", "Line 2", "Line 3" }, GetTextLines(result));
        }

        [TestMethod]
        public void ApplyFilters_WithTrueFilterAndPositiveContext_ReturnsAllLinesOnce()
        {
            // Arrange
            var doc = new LogDocument();
            doc.AppendLine("Line 1");
            doc.AppendLine("Line 2");
            doc.AppendLine("Line 3");

            // Act
            var result = FilterEngine.ApplyFilters(doc, new TrueFilter(), contextLines: 1);

            // Assert: Although context is applied, duplicate lines should be avoided.
            Assert.AreEqual(3, result.Count);
            CollectionAssert.AreEqual(new List<string> { "Line 1", "Line 2", "Line 3" }, GetTextLines(result));
        }

        [TestMethod]
        public void ApplyFilters_WithSubstringFilter_ReturnsMatchingLinesWithContext()
        {
            // Arrange
            var doc = new LogDocument();
            // Log lines: some contain the word "Error"
            doc.AppendLine("Info: Everything ok");
            doc.AppendLine("Error: Something went wrong");
            doc.AppendLine("Info: Continuing operation");
            doc.AppendLine("Error: Critical failure");

            // Use a SubstringFilter to match lines with "Error".
            var substringFilter = new SubstringFilter("Error");

            // Act: Use a context of 1 line.
            var result = FilterEngine.ApplyFilters(doc, substringFilter, contextLines: 1);

            // Assert:
            // For line index 1: include lines 0,1,2.
            // For line index 3: include lines 2,3.
            // Expected unique lines: "Info: Everything ok", "Error: Something went wrong", "Info: Continuing operation", "Error: Critical failure"
            var expected = new List<string>
            {
                "Info: Everything ok",
                "Error: Something went wrong",
                "Info: Continuing operation",
                "Error: Critical failure"
            };
            CollectionAssert.AreEqual(expected, GetTextLines(result));
        }

        [TestMethod]
        public void ApplyFilters_WithAndFilter_ReturnsOnlyLinesMatchingBothConditions()
        {
            // Arrange
            var doc = new LogDocument();
            // Log lines: only one line should contain both "Error" and "Critical".
            doc.AppendLine("Info: Everything ok");
            doc.AppendLine("Error: Minor error");
            doc.AppendLine("Critical: Major error");
            doc.AppendLine("Error: Critical failure");

            // Create an AndFilter: matches lines containing both "Error" and "Critical".
            var andFilter = new AndFilter();
            andFilter.Add(new SubstringFilter("Error"));
            andFilter.Add(new SubstringFilter("Critical"));

            // Act: No context (contextLines = 0)
            var result = FilterEngine.ApplyFilters(doc, andFilter, contextLines: 0);

            // Assert: Only "Error: Critical failure" should match.
            Assert.AreEqual(1, result.Count);
            CollectionAssert.AreEqual(new List<string> { "Error: Critical failure" }, GetTextLines(result));
        }
    }
}
