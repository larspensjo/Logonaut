using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Logonaut.UI.Helpers
{
    /// <summary>
    /// Colorizes the background of a specific line number in the document.
    /// </summary>
    public class SelectedIndexHighlightTransformer : DocumentColorizingTransformer
    {
        /// <summary>
        /// Gets or sets the 1-based line number to highlight.
        /// Set to -1 to disable highlighting.
        /// </summary>
        public int HighlightedLineNumber { get; set; } = -1;

        /// <summary>
        /// Gets or sets the Brush used for highlighting the background.
        /// </summary>
        public Brush? HighlightBrush { get; set; }

        protected override void ColorizeLine(DocumentLine line)
        {
            // Check if this is the line to highlight and we have a valid brush
            if (!line.IsDeleted && line.LineNumber == HighlightedLineNumber && HighlightBrush != null)
            {
                // Apply the highlighting to the entire line segment
                // The start offset is line.Offset
                // The end offset is line.EndOffset (end of line content, excluding delimiter)
                base.ChangeLinePart(line.Offset, line.EndOffset, ApplyHighlightingAction);
            }
        }

        /// <summary>
        /// Action passed to ChangeLinePart to modify the visual element.
        /// </summary>
        private void ApplyHighlightingAction(VisualLineElement element)
        {
            // Apply the background brush
            element.BackgroundBrush = HighlightBrush;

            // Optional: Modify other properties like foreground if needed
            // element.TextRunProperties.SetForegroundBrush(Brushes.White);
        }

        /// <summary>
        /// Helper method to update the state and trigger redraw if needed.
        /// Should be called from the UI thread.
        /// </summary>
        /// <param name="lineNumber">The new 1-based line number to highlight (-1 for none).</param>
        /// <param name="brush">The brush to use.</param>
        /// <param name="textView">The TextView to redraw.</param>
        public void UpdateState(int lineNumber, Brush? brush, TextView textView)
        {
            bool changed = false;
            if (HighlightedLineNumber != lineNumber)
            {
                HighlightedLineNumber = lineNumber;
                changed = true;
            }
            if (HighlightBrush != brush)
            {
                HighlightBrush = brush;
                // Attempt to freeze the brush for performance
                if (HighlightBrush != null && HighlightBrush.CanFreeze && !HighlightBrush.IsFrozen)
                {
                    HighlightBrush.Freeze();
                }
                changed = true;
            }

            if (changed && textView != null)
            {
                // Trigger a redraw of the text view to apply the changes
                textView.Redraw();
            }
        }
    }
}