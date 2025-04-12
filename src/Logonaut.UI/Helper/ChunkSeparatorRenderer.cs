using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows; // Required for DependencyObject, DependencyProperty etc.
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common;

namespace Logonaut.UI.Helpers
{
    /// <summary>
    /// A custom renderer that draws theme-aware lines between chunks of filtered log lines.
    /// </summary>
    // Inherit from DependencyObject to support Dependency Properties
    public class ChunkSeparatorRenderer : DependencyObject, IBackgroundRenderer, IDisposable
    {
        private readonly TextView _textView;
        private Pen? _separatorPen; // Pen is now created based on the brush DP
        private IReadOnlyList<FilteredLogLine>? _filteredLines;

        #region SeparatorBrush Dependency Property

        public static readonly DependencyProperty SeparatorBrushProperty =
            DependencyProperty.Register(
                nameof(SeparatorBrush),
                typeof(Brush),
                typeof(ChunkSeparatorRenderer),
                new FrameworkPropertyMetadata(
                    Brushes.Gray, // Default value if binding fails
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnSeparatorBrushChanged)); // Callback when the brush changes

        public Brush SeparatorBrush
        {
            get => (Brush)GetValue(SeparatorBrushProperty);
            set => SetValue(SeparatorBrushProperty, value);
        }

        private static void OnSeparatorBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var renderer = (ChunkSeparatorRenderer)d;
            renderer.UpdatePen();
            // Invalidation is handled by AffectsRender flag, but can force redraw if needed:
            // renderer._textView?.InvalidateLayer(renderer.Layer);
        }

        #endregion

        public ChunkSeparatorRenderer(TextView textView)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            // Initial pen update based on potentially already set DP value (or default)
            UpdatePen();

            // Subscribe to scroll events
            _textView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
        }

        // Method to create/update the pen based on the SeparatorBrush DP
        private void UpdatePen()
        {
            Brush brush = SeparatorBrush;

            // Try to freeze the brush for performance if it's not already
            if (brush.CanFreeze && !brush.IsFrozen)
            {
                brush.Freeze();
            }

            var newPen = new Pen(brush, 1);

            // Try to freeze the pen
            if (newPen.CanFreeze)
            {
                newPen.Freeze();
            }
            _separatorPen = newPen;
        }


        private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
        {
            _textView.InvalidateLayer(Layer);
        }

        public void Dispose()
        {
            _textView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
            // Clear dependency property bindings if necessary (usually handled by GC)
            // this.ClearValue(SeparatorBrushProperty);
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            // Use the _separatorPen created from the DP
            Pen pen = _separatorPen;
            if (pen == null || _filteredLines == null || _filteredLines.Count < 2 || !textView.VisualLinesValid)
                return;

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

                    // Draw line ABOVE current line if gap > 1 (indicates new chunk)
                    if (gap > 1) // <<< Ensure this is gap > 1 for chunk boundaries
                    // if (gap > 0) // Use for testing drawing between *every* line
                    {
                        double y = currentVisualLine.VisualTop - verticalOffset;
                        y = Math.Floor(y) + 0.5; // Pixel snapping

                        if (y >= 0 && y <= viewPortHeight)
                        {
                            drawingContext.DrawLine(pen, new Point(0, y), new Point(textView.ActualWidth, y));
                            // System.Diagnostics.Debug.WriteLine($"Gap={gap}. Draw above OrigLine:{currentFilteredLine.OriginalLineNumber} at Y={y}");
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
