// Logonaut.UI/Helpers/PersistentLineHighlightRenderer.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace Logonaut.UI.Helpers
{
    /// <summary>
    /// Renders a background highlight for a specific line index provided via a Dependency Property.
    /// </summary>
    public class PersistentLineHighlightRenderer : DependencyObject, IBackgroundRenderer
    {
        private readonly TextView _textView;

        #region Dependency Properties

        // HighlightedLineIndex: The 0-based index within the *filtered* document to highlight.
        public static readonly DependencyProperty HighlightedLineIndexProperty =
            DependencyProperty.Register(nameof(HighlightedLineIndex), typeof(int), typeof(PersistentLineHighlightRenderer),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

        public int HighlightedLineIndex
        {
            get => (int)GetValue(HighlightedLineIndexProperty);
            set => SetValue(HighlightedLineIndexProperty, value);
        }

        // HighlightBrush: The brush used for highlighting.
        public static readonly DependencyProperty HighlightBrushProperty =
            DependencyProperty.Register(nameof(HighlightBrush), typeof(Brush), typeof(PersistentLineHighlightRenderer),
                new FrameworkPropertyMetadata(Brushes.LightYellow, FrameworkPropertyMetadataOptions.AffectsRender)); // Default color

        public Brush HighlightBrush
        {
            get => (Brush)GetValue(HighlightBrushProperty);
            set => SetValue(HighlightBrushProperty, value);
        }

        #endregion

        public KnownLayer Layer => KnownLayer.Background; // Draw behind text

        public PersistentLineHighlightRenderer(TextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            // Consider subscribing to TextView scroll/layout changes if needed, but AffectsRender should handle most cases.
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            int lineIndexToHighlight = HighlightedLineIndex;
            if (lineIndexToHighlight < 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
            {
                return; // Nothing to highlight or data not ready
            }

            // Find the VisualLine corresponding to the index in the *filtered* document
            // Remember VisualLine.FirstDocumentLine.LineNumber is 1-based
            VisualLine? visualLine = textView.VisualLines
                                        .FirstOrDefault(vl => vl.FirstDocumentLine.LineNumber == (lineIndexToHighlight + 1));

            if (visualLine != null)
            {
                // Get the background bounds for the visual line
                var lineBounds = BackgroundGeometryBuilder.GetRectsFromVisualSegment(textView, visualLine, 0, visualLine.VisualLength).FirstOrDefault();

                if (!lineBounds.IsEmpty)
                {
                    // Adjust Y position for scrolling
                    double yPos = lineBounds.Top - textView.VerticalOffset;

                    // Ensure it's within the visible area before drawing
                    if (yPos + lineBounds.Height > 0 && yPos < textView.ActualHeight)
                    {
                        // Use the bound HighlightBrush
                        drawingContext.DrawRectangle(
                            HighlightBrush,
                            null, // No border
                            new Rect(0, yPos, textView.ActualWidth, lineBounds.Height)
                        );
                    }
                }
            }
        }
    }
}