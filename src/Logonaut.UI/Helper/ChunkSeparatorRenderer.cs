using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common;
using System; // For Math
using System.Linq; // For FirstOrDefault
using System.Collections.Generic; // For List

namespace Logonaut.UI.Helpers
{
    public class ChunkSeparatorRenderer : IBackgroundRenderer, IDisposable
    {
        private readonly TextView _textView;
        private Pen? _separatorPen; // NEON: Make nullable, create based on theme
        private IReadOnlyList<FilteredLogLine>? _filteredLines;

        public ChunkSeparatorRenderer(TextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            UpdatePen(); // NEON: Initialize pen based on current theme

            _textView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
            // NEON: Consider listening for theme changes if TextView doesn't automatically trigger redraws
            // For simplicity, we'll rely on redraws caused by other actions for now.
        }

        // NEON: Method to update the pen based on theme resource
        private void UpdatePen()
        {
             Brush defaultBrush = Brushes.LightGray; // Fallback
             Brush? brush = _textView?.TryFindResource("DividerBrush") as Brush ?? defaultBrush;

             var pen = new Pen(brush, 1.0);
             if (pen.CanFreeze)
             {
                 pen.Freeze();
             }
             _separatorPen = pen;
        }


        private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
        {
            _textView.InvalidateLayer(Layer);
        }

        public void Dispose()
        {
            _textView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            // NEON: Update pen in case theme changed without TextView change event (less likely needed but safer)
            // This could be optimized by only updating if theme actually changed.
            UpdatePen();

            if (_filteredLines == null || _filteredLines.Count < 2 || !textView.VisualLinesValid || _separatorPen == null) // NEON: Check pen
                return;

            // ... (rest of the drawing logic remains the same) ...
             var avalonVisualLines = textView.VisualLines;
            if (avalonVisualLines.Count < 1)
                return;

            double viewPortHeight = textView.ActualHeight;
            double verticalOffset = textView.VerticalOffset;

             var filteredLineLookup = _filteredLines.Select((line, index) => new { Index = index + 1, Line = line })
                                                   .ToDictionary(item => item.Index, item => item.Line);

            for (int i = 1; i < avalonVisualLines.Count; i++)
            {
                VisualLine currentVisualLine = avalonVisualLines[i];
                VisualLine previousVisualLine = avalonVisualLines[i - 1];

                if (filteredLineLookup.TryGetValue(currentVisualLine.FirstDocumentLine.LineNumber, out var currentFilteredLine) &&
                    filteredLineLookup.TryGetValue(previousVisualLine.FirstDocumentLine.LineNumber, out var prevFilteredLine))
                {
                    int gap = currentFilteredLine.OriginalLineNumber - prevFilteredLine.OriginalLineNumber;

                    if (gap > 1)
                    {
                        double y = currentVisualLine.VisualTop - verticalOffset;
                        y = Math.Floor(y) + 0.5;

                        if (y >= 0 && y <= viewPortHeight)
                        {
                            var startPoint = new Point(0, y);
                            var endPoint = new Point(textView.ActualWidth, y);
                            drawingContext.DrawLine(_separatorPen, startPoint, endPoint); // NEON: Use the potentially themed pen
                        }
                    }
                }
            }
        }

        public void UpdateChunks(IReadOnlyList<FilteredLogLine> filteredLines, int contextLines)
        {
            _filteredLines = filteredLines;
            _textView.InvalidateLayer(Layer);
        }
    }
}
