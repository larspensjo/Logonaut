using Logonaut.Common;
using Logonaut.Filters;

using System.Diagnostics;

namespace Logonaut.Core;

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

    /// <summary>
    /// Applies a filter to a subset of new log lines, including context lines
    /// looked up from the full log document.
    /// </summary>
    /// <param name="newLinesInfo">Information about the new lines being processed (text and original 0-based index).</param>
    /// <param name="fullLogDoc">The complete log document for context lookups.</param>
    /// <param name="filterTree">The filter rules to apply.</param>
    /// <param name="contextLines">The number of context lines before and after a match to include.</param>
    /// <returns>A read-only list of FilteredLogLine objects (matches and context) relevant to the input lines, ordered by original line number.</returns>
    public static IReadOnlyList<FilteredLogLine> ApplyFilterToSubset(
        IEnumerable<OriginalLineInfo> newLinesInfo,
        LogDocument fullLogDoc,
        IFilter filterTree,
        int contextLines)
    {
        // Use SortedDictionary to maintain order and uniqueness by original line number (1-based key).
        var results = new SortedDictionary<int, FilteredLogLine>();
        IReadOnlyList<string>? fullLinesSnapshot = null; // Lazy load snapshot only if context needed

        foreach (var lineInfo in newLinesInfo)
        {
            // Only evaluate lines that actually exist in the LogDocument
            // (Handles potential race conditions if LogDoc cleared during processing)
            if (lineInfo.OriginalIndex < 0 || lineInfo.OriginalIndex >= fullLogDoc.Count)
            {
                Debug.WriteLine($"WARN: ApplyFilterToSubset skipping line with out-of-bounds index {lineInfo.OriginalIndex}. LogDoc Count: {fullLogDoc.Count}");
                continue;
            }

            // Check if the *new* line matches the filter
            if (filterTree.IsMatch(lineInfo.Text))
            {
                int matchedOriginalLineNumber = lineInfo.OriginalIndex + 1; // 1-based

                // Ensure the matched line itself is in the results and marked as not context
                if (!results.ContainsKey(matchedOriginalLineNumber))
                {
                    results.Add(matchedOriginalLineNumber, new FilteredLogLine(matchedOriginalLineNumber, lineInfo.Text, false));
                }
                else if (results[matchedOriginalLineNumber].IsContextLine) // If previously added as context, mark as match
                {
                    results[matchedOriginalLineNumber] = new FilteredLogLine(matchedOriginalLineNumber, lineInfo.Text, false);
                }

                // --- Context Line Handling ---
                if (contextLines > 0)
                {
                    // Load the full document snapshot only when context is actually needed
                    fullLinesSnapshot ??= fullLogDoc.ToList();

                    // ** Context Before **
                    // Calculate the range of original indices (0-based)
                    int startContextIndex = Math.Max(0, lineInfo.OriginalIndex - contextLines);
                    for (int ctxIndex = startContextIndex; ctxIndex < lineInfo.OriginalIndex; ctxIndex++)
                    {
                        int ctxLineNumber = ctxIndex + 1; // 1-based
                        if (!results.ContainsKey(ctxLineNumber)) // Add only if not already included
                        {
                            // Ensure index is valid for the snapshot (should be, but belt-and-suspenders)
                            if (ctxIndex >= 0 && ctxIndex < fullLinesSnapshot.Count)
                            {
                                results.Add(ctxLineNumber, new FilteredLogLine(ctxLineNumber, fullLinesSnapshot[ctxIndex], true));
                            }
                            else {
                                    Debug.WriteLine($"WARN: ApplyFilterToSubset skipping context-before line with out-of-bounds index {ctxIndex}. Snapshot Count: {fullLinesSnapshot.Count}");
                            }
                        }
                    }

                    // ** Context After **
                    // Calculate the range of original indices (0-based)
                    int endContextIndex = Math.Min(fullLinesSnapshot.Count - 1, lineInfo.OriginalIndex + contextLines);
                    for (int ctxIndex = lineInfo.OriginalIndex + 1; ctxIndex <= endContextIndex; ctxIndex++)
                    {
                        int ctxLineNumber = ctxIndex + 1; // 1-based
                        if (!results.ContainsKey(ctxLineNumber)) // Add only if not already included
                        {
                                // Ensure index is valid for the snapshot
                            if (ctxIndex >= 0 && ctxIndex < fullLinesSnapshot.Count)
                            {
                                results.Add(ctxLineNumber, new FilteredLogLine(ctxLineNumber, fullLinesSnapshot[ctxIndex], true));
                            }
                            else {
                                    Debug.WriteLine($"WARN: ApplyFilterToSubset skipping context-after line with out-of-bounds index {ctxIndex}. Snapshot Count: {fullLinesSnapshot.Count}");
                            }
                        }
                    }
                }
            }
        }

        // The SortedDictionary ensures lines are ordered correctly by original line number.
        return results.Values.ToList().AsReadOnly();
    }
}

public record OriginalLineInfo(int OriginalIndex, string Text);
