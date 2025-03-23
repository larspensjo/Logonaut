using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Common;
using System.Linq;

namespace Logonaut.Common.Tests
{
    [TestClass]
    public class LogDocumentTests
    {
        [TestMethod]
        public void AppendLine_IncreasesCount()
        {
            // Arrange
            var doc = new LogDocument();
            Assert.AreEqual(0, doc.Count);

            // Act
            doc.AppendLine("Line 1");

            // Assert
            Assert.AreEqual(1, doc.Count);
        }

        [TestMethod]
        public void GetLines_ReturnsCorrectSubset()
        {
            // Arrange
            var doc = new LogDocument();
            for (int i = 0; i < 10; i++)
            {
                doc.AppendLine($"Line {i + 1}");
            }

            // Act
            var subset = doc.GetLines(2, 5);

            // Assert
            Assert.AreEqual(5, subset.Count, "Should return 5 lines.");
            Assert.AreEqual("Line 3", subset[0], "First line in subset should be 'Line 3'.");
            Assert.AreEqual("Line 7", subset[4], "Last line in subset should be 'Line 7'.");
        }

        [TestMethod]
        public void Indexer_ReturnsCorrectLine()
        {
            // Arrange
            var doc = new LogDocument();
            doc.AppendLine("Test Line");

            // Act
            var line = doc[0];

            // Assert
            Assert.AreEqual("Test Line", line);
        }

        [TestMethod]
        public void Clear_RemovesAllLines()
        {
            // Arrange
            var doc = new LogDocument();
            doc.AppendLine("Line 1");
            doc.AppendLine("Line 2");

            // Act
            doc.Clear();

            // Assert
            Assert.AreEqual(0, doc.Count, "Document should be empty after clear.");
        }

        [TestMethod]
        public void GetLines_StartIndexBeyondCount_ReturnsEmptyCollection()
        {
            // Arrange
            var doc = new LogDocument();
            doc.AppendLine("Line 1");

            // Act
            var subset = doc.GetLines(5, 3);

            // Assert
            Assert.AreEqual(0, subset.Count, "Should return an empty collection when start index is out of range.");
        }

        [TestMethod]
        public void AddInitialLines_AddsLinesCorrectly()
        {
            // Arrange
            var doc = new LogDocument();
            string initialLines = "Line 1\r\nLine 2\r\nLine 3\r\n";

            // Act
            doc.AddInitialLines(initialLines);

            // Assert
            Assert.AreEqual(3, doc.Count, "Document should contain 3 lines.");
            Assert.AreEqual("Line 1", doc[0], "First line should be 'Line 1'.");
            Assert.AreEqual("Line 2", doc[1], "Second line should be 'Line 2'.");
            Assert.AreEqual("Line 3", doc[2], "Third line should be 'Line 3'.");
        }

        [TestMethod]
        public void AsReadOnly_ReturnsAllLines()
        {
            // Arrange
            var doc = new LogDocument();
            doc.AppendLine("Line 1");
            doc.AppendLine("Line 2");

            // Act
            var readOnlyLines = doc.ToList();

            // Assert
            Assert.AreEqual(2, readOnlyLines.Count, "Read-only list should contain all lines.");
            Assert.AreEqual("Line 1", readOnlyLines[0], "First line should be 'Line 1'.");
            Assert.AreEqual("Line 2", readOnlyLines[1], "Second line should be 'Line 2'.");
        }
    }
}
