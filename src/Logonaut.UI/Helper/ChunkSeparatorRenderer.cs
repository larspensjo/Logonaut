using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common;
using System; // For Math
using System.Linq; // For FirstOrDefault
using System.Collections.Generic; // For List

namespace Logonaut.UI.Helpers
{
    /// <summary>
    /// A custom renderer that draws thin lines between chunks of filtered log lines.
    /// A chunk is a group of consecutive lines containing a matched line and its context.
    /// </summary>
    public class ChunkSeparatorRenderer : IBackgroundRenderer, IDisposable
    {
        private readonly TextView _textView;
        private readonly Pen _separatorPen;
        private IReadOnlyList<FilteredLogLine>? _filteredLines;
        // private int _contextLines; // ContextLines isn't directly needed for drawing logic if using gap > 1

        public ChunkSeparatorRenderer(TextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            // Create a light gray pen for the separator lines
            var brush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            brush.Freeze();
            _separatorPen = new Pen(brush, 1);
            _separatorPen.Freeze();

            // Subscribe to scroll events
            _textView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
        }

        private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
        {
            // Redraw when scrolling
            _textView.InvalidateLayer(Layer);
        }

        public void Dispose()
        {
            // Unsubscribe from events
            _textView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_filteredLines == null || _filteredLines.Count < 2 || !textView.VisualLinesValid) // Need at least 2 lines to have a gap
                return;

            // This is the AvalonEdit.Rendering.VisualLine. They are a representation of a line in the text view.
            // As long as the window is not resized, the VisualLines.Count is stable.
            var avalonVisualLines = textView.VisualLines;
            if (avalonVisualLines.Count < 1) // Need at least one VISIBLE line to potentially draw above it
                return;

            // Cache properties for efficiency
            double viewPortHeight = textView.ActualHeight;
            double verticalOffset = textView.VerticalOffset; // The amount scrolled down

             // Create a quick lookup: DocumentLineNumber -> FilteredLogLine
             // This avoids iterating _filteredLines inside the loop.
             // DocumentLineNumber is the 1-based index in the *filtered* document.
             var filteredLineLookup = _filteredLines.Select((line, index) => new { Index = index + 1, Line = line })
                                                   .ToDictionary(item => item.Index, item => item.Line);

            // Iterate through the VISIBLE visual lines, starting from the second one
            for (int i = 1; i < avalonVisualLines.Count; i++)
            {
                VisualLine currentVisualLine = avalonVisualLines[i];
                VisualLine previousVisualLine = avalonVisualLines[i - 1];

                // Get the corresponding FilteredLogLine objects using the lookup
                if (filteredLineLookup.TryGetValue(currentVisualLine.FirstDocumentLine.LineNumber, out var currentFilteredLine) &&
                    filteredLineLookup.TryGetValue(previousVisualLine.FirstDocumentLine.LineNumber, out var prevFilteredLine))
                {
                    int gap = currentFilteredLine.OriginalLineNumber - prevFilteredLine.OriginalLineNumber;

                    // Draw a line ABOVE the currentVisualLine if the gap indicates a new chunk
                    if (gap > 1)
                    {
                        // VisualTop is relative to the whole document. Subtract VerticalOffset for viewport coordinates.
                        double y = currentVisualLine.VisualTop - verticalOffset;

                        // Align to pixel center for sharpness
                        y = Math.Floor(y) + 0.5;

                        // --- Draw only if within viewport bounds ---
                        if (y >= 0 && y <= viewPortHeight)
                        {
                            var startPoint = new Point(0, y);
                            var endPoint = new Point(textView.ActualWidth, y);
                            drawingContext.DrawLine(_separatorPen, startPoint, endPoint);
                        }
                    }
                }
            }
        }

        public void UpdateChunks(IReadOnlyList<FilteredLogLine> filteredLines, int contextLines)
        {
            _filteredLines = filteredLines;
            // _contextLines = contextLines; // Store if needed later, but gap logic is sufficient
            _textView.InvalidateLayer(Layer);
        }
    }
}