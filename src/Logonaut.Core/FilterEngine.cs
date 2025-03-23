using System.Collections.Generic;
using System.Linq;
using Logonaut.Common;
using Logonaut.Filters;

namespace Logonaut.Core
{
    // FilterEngine processes the entire log off the UI thread, applying filters and context,
    // and produces a complete, updated filtered list. This ensures responsiveness and atomic UI updates,
    // even when handling large log files.
    public static class FilterEngine
    {
        // Applies the filter to the log document and returns a filtered list.
        public static IReadOnlyList<string> ApplyFilters(LogDocument logDoc, IFilter filterTree, int contextLines = 0)
        {
            var allLines = logDoc.ToList(); // Use the complete read-only list
            var filteredLines = new List<string>();

            for (int i = 0; i < allLines.Count; i++)
            {
                if (filterTree.IsMatch(allLines[i]))
                {
                    // Include context lines if needed.
                    int start = i - contextLines < 0 ? 0 : i - contextLines;
                    int end = i + contextLines >= allLines.Count ? allLines.Count - 1 : i + contextLines;
                    for (int j = start; j <= end; j++)
                    {
                        if (!filteredLines.Contains(allLines[j]))
                        {
                            filteredLines.Add(allLines[j]);
                        }
                    }
                }
            }
            return filteredLines;
        }
    }
}
