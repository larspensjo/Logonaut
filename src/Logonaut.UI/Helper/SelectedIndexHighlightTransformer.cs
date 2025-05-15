using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common; // Required for FilteredLogLine
using System.Collections.Generic; // Required for IReadOnlyList
using System.Linq; // Required for LINQ operations like FirstOrDefault

namespace Logonaut.UI.Helpers;

/// <summary>
/// Colorizes the background of a specific line based on its original line number.
/// </summary>
public class SelectedIndexHighlightTransformer : DocumentColorizingTransformer
{
    /// <summary>
    /// Gets or sets the 1-based ORIGINAL line number to highlight.
    /// Set to -1 to disable highlighting.
    /// </summary>
    public int HighlightedOriginalLineNumber { get; set; } = -1;

    /// <summary>
    /// Gets or sets the Brush used for highlighting the background.
    /// </summary>
    public Brush? HighlightBrush { get; set; }

    /// <summary>
    /// Gets or sets the current list of filtered log lines being displayed.
    /// This is crucial for mapping AvalonEdit's DocumentLine to our FilteredLogLine.
    /// </summary>
    public IReadOnlyList<FilteredLogLine>? FilteredLinesSource { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.IsDeleted || HighlightedOriginalLineNumber == -1 || HighlightBrush == null || FilteredLinesSource == null)
            return;

        // AvalonEdit's line.LineNumber is 1-based, corresponding to the index in the current view.
        int currentViewLineIndex = line.LineNumber - 1;

        if (currentViewLineIndex >= 0 && currentViewLineIndex < FilteredLinesSource.Count)
        {
            FilteredLogLine currentFilteredLogLine = FilteredLinesSource[currentViewLineIndex];
            if (currentFilteredLogLine.OriginalLineNumber == HighlightedOriginalLineNumber)
            {
                base.ChangeLinePart(line.Offset, line.EndOffset, ApplyHighlightingAction);
            }
        }
    }

    /// <summary>
    /// Action passed to ChangeLinePart to modify the visual element.
    /// </summary>
    private void ApplyHighlightingAction(VisualLineElement element)
    {
        // Apply the background brush
        element.BackgroundBrush = HighlightBrush;
    }

    /// <summary>
    /// Helper method to update the state and trigger redraw if needed.
    /// Should be called from the UI thread.
    /// </summary>
    /// <param name="originalLineNumber">The new 1-based original line number to highlight (-1 for none).</param>
    /// <param name="brush">The brush to use.</param>
    /// <param name="filteredLines">The current collection of filtered lines being displayed.</param>
    /// <param name="textView">The TextView to redraw.</param>
    public void UpdateState(int originalLineNumber, Brush? brush, IReadOnlyList<FilteredLogLine>? filteredLines, TextView textView)
    {
        bool changed = false;
        if (HighlightedOriginalLineNumber != originalLineNumber)
        {
            HighlightedOriginalLineNumber = originalLineNumber;
            changed = true;
        }
        if (HighlightBrush != brush)
        {
            HighlightBrush = brush;
            if (HighlightBrush != null && HighlightBrush.CanFreeze && !HighlightBrush.IsFrozen)
            {
                HighlightBrush.Freeze();
            }
            changed = true;
        }
        // Also check if the FilteredLinesSource reference has changed,
        // as this is critical for correct mapping.
        if (FilteredLinesSource != filteredLines)
        {
            FilteredLinesSource = filteredLines;
            changed = true; // If the source collection changes, we definitely need to re-evaluate.
        }


        if (changed && textView != null)
        {
            textView.Redraw();
        }
    }
}
