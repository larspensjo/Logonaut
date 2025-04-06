using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Logonaut.UI.Helpers
{
    /// <summary>
    /// A simple AvalonEdit margin that draws a vertical line.
    /// Typically used as a separator between other margins or the text area.
    /// </summary>
    public class VerticalLineMargin : AbstractMargin
    {
        private Pen? _linePen;

        // Optional DependencyProperty for color customization if needed later
        // public static readonly DependencyProperty LineColorProperty = ...

        public VerticalLineMargin()
        {
            // Initialize the pen - consider using a theme resource later
            _linePen = CreateDefaultPen();
        }

        private Pen CreateDefaultPen()
        {
            // Use a standard Gray. Consider making this theme-aware later.
            var pen = new Pen(Brushes.Gray, 1.0);
            // Ensure the pen is frozen for performance, as it won't change dynamically here
            pen.Freeze();
            return pen;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // This margin only draws a line, so it needs minimal width.
            // 1 pixel is usually sufficient.
            return new Size(1, 0); // Width=1, Height is determined by TextView
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var renderSize = RenderSize;
            var pen = _linePen; // Use the cached pen

            if (pen == null || renderSize.Width <= 0) return;

            // Draw the line vertically centered within the allocated width.
            // Using 0.5 aligns it nicely on pixel boundaries.
            double x = renderSize.Width / 2.0;

            // Align to the nearest pixel center for sharpness
            x = Math.Floor(x) + 0.5;

            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, renderSize.Height));
        }

        // Optional: Handle TextView changes if the pen color needs to react to themes
        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            base.OnTextViewChanged(oldTextView, newTextView);
            // If you implement theme-aware pen color, update it here
            // e.g., _linePen = CreateThemeAwarePen(newTextView);
            InvalidateVisual(); // Redraw with potentially new pen
        }
    }
}