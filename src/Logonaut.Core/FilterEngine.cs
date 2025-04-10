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
        public static IReadOnlyList<FilteredLogLine> ApplyFilters(LogDocument logDoc, IFilter filterTree, int contextLines = 0) 
        {
            var allLines = logDoc.ToList(); // Use the complete read-only list
            // Use a dictionary to store FilteredLogLine by original line number to ensure uniqueness and order
            var filteredLinesDict = new SortedDictionary<int, FilteredLogLine>(); 

            for (int i = 0; i < allLines.Count; i++)
            {
                if (filterTree.IsMatch(allLines[i]))
                {
                    // Add the matched line first
                    int matchedLineNumber = i + 1;
                    if (!filteredLinesDict.ContainsKey(matchedLineNumber))
                    {
                        filteredLinesDict.Add(matchedLineNumber, new FilteredLogLine(matchedLineNumber, allLines[i], false));
                    }

                    if (contextLines > 0)
                    {
                        // Add context lines before the match
                        for (int j = Math.Max(0, i - contextLines); j < i; j++)
                        {
                            int contextLineNumber = j + 1;
                            if (!filteredLinesDict.ContainsKey(contextLineNumber))
                            {
                                filteredLinesDict.Add(contextLineNumber, new FilteredLogLine(contextLineNumber, allLines[j], true));
                            }
                        }

                        // Add context lines after the match
                        for (int j = i + 1; j <= Math.Min(allLines.Count - 1, i + contextLines); j++)
                        {
                            int contextLineNumber = j + 1;
                            if (!filteredLinesDict.ContainsKey(contextLineNumber))
                            {
                                filteredLinesDict.Add(contextLineNumber, new FilteredLogLine(contextLineNumber, allLines[j], true));
                            }
                        }
                    }
                }
            }
            // Return the values (FilteredLogLine objects) in their original line number order.
            return filteredLinesDict.Values.ToList().AsReadOnly();
        }
    }
}
