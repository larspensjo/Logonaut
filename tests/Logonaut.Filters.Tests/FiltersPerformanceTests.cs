using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;         // your SubstringFilter, FilterEngine, etc.
using Logonaut.Core;
using Logonaut.Common;          // if you need FilterProfile, etc.

namespace Logonaut.Filters.Tests
{
    [TestClass]
    public class PerformanceTests
    {
        private const long TargetSizeBytes = 50L * 1024 * 1024; // 50 MB

        /// <summary>
        /// Generate ~50 MB of log lines, ~1% of which contain "ERROR",
        /// then measure how long FilterEngine takes to pick them out.
        /// </summary>
        [TestMethod]
        [TestCategory("Performance")]
        public void FilterEngine_On50MbPayload_ShouldCompleteQuickly()
        {
            // --- Arrange: build 50 MB payload ...
            // Calculate approximate line length for better capacity estimation
            const int avgLineLength = 100; // typical log line length including newline
            var sb = new StringBuilder((int)(TargetSizeBytes + (TargetSizeBytes / avgLineLength * 2))); // account for newlines
            
            int lineIndex = 0;
            while (sb.Length < TargetSizeBytes)
            {
                // every 100th line contains "ERROR"
                string timestamp = DateTime.UtcNow.ToString("O");
                if (lineIndex % 100 == 0)
                    sb.AppendLine($"{timestamp} [ERROR] Failure in component at iteration {lineIndex}");
                else
                    sb.AppendLine($"{timestamp} [INFO] Normal operation at iteration {lineIndex}");
                lineIndex++;
            }
            string[] allLines = sb.ToString().Split(new[]{ "\r\n","\n" }, StringSplitOptions.RemoveEmptyEntries);

            // Create and populate LogDocument
            var logDoc = new LogDocument();
            foreach (var line in allLines)
                logDoc.AppendLine(line);

            // SubstringFilter matches "ERROR"
            var filter = new SubstringFilter("ERROR");

            // Expected count
            int expectedMatches = allLines.Count(l => l.Contains("ERROR"));

            // --- Act & time it ---
            var sw = Stopwatch.StartNew();
            var filteredLines = FilterEngine.ApplyFilters(logDoc, filter, contextLines: 0);
            sw.Stop();

            // --- Assert correctness ---
            Assert.AreEqual(expectedMatches, filteredLines.Count, "Wrong number of matches.");

            // --- Optional performance check ---
            TestContext?.WriteLine($"Filtered {allLines.Length:N0} lines in {sw.Elapsed.TotalSeconds:F2}s");
            Assert.IsTrue(sw.Elapsed.TotalSeconds < 2.0, $"Filtering took {sw.Elapsed.TotalSeconds:F2}s, exceeding budget.");
        }

        public TestContext? TestContext { get; set; }
    }
}
