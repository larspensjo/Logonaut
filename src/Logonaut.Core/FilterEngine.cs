using Logonaut.Common;
using Logonaut.Filters;

namespace Logonaut.Core
{
    // FilterEngine processes the entire log off the UI thread, applying filters and context,
    // and produces a complete, updated filtered list. This ensures responsiveness and atomic UI updates,
    // even when handling large log files.
    public static class FilterEngine
    {
        // Applies the filter to the log document and returns a filtered list
        // containing FilteredLogLine objects with original line numbers.
        public static IReadOnlyList<FilteredLogLine> ApplyFilters(LogDocument logDoc, IFilter filterTree, int contextLines = 0) // <<<<< CHANGED RETURN TYPE
        {
            var allLines = logDoc.ToList(); // Use the complete read-only list
            // Use a dictionary to store FilteredLogLine by original line number to ensure uniqueness and order
            var filteredLinesDict = new SortedDictionary<int, FilteredLogLine>(); // <<<<< CHANGED TYPE

            for (int i = 0; i < allLines.Count; i++)
            {
                if (filterTree.IsMatch(allLines[i]))
                {
                    // Determine the range of lines to include (match + context)
                    int start = Math.Max(0, i - contextLines);
                    int end = Math.Min(allLines.Count - 1, i + contextLines);

                    for (int j = start; j <= end; j++)
                    {
                        int originalLineNumber = j + 1; // 1-based index

                        // Add to dictionary only if the original line number isn't already present
                        if (!filteredLinesDict.ContainsKey(originalLineNumber))
                        {
                            filteredLinesDict.Add(originalLineNumber, new FilteredLogLine(originalLineNumber, allLines[j]));
                        }
                    }
                }
            }
            // Return the values (FilteredLogLine objects) in their original line number order.
            return filteredLinesDict.Values.ToList().AsReadOnly();
        }
    }
}
