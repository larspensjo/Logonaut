using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace Logonaut.UI.Helpers;

public class VerticalLineMargin : AbstractMargin
{
    private Pen? _linePen;

    // NEON: Use DynamicResource lookup for the pen brush
    private Pen CreateThemeAwarePen(TextView? textView)
    {
        // Default color if resource not found or textview is null
        Brush defaultBrush = Brushes.Gray;
        Brush? brush = textView?.TryFindResource("DividerBrush") as Brush ?? defaultBrush;

        // Ensure the pen is frozen for performance
        var pen = new Pen(brush, 1.0);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
        return pen;
    }

    public VerticalLineMargin()
    {
        // Pen will be created/updated in OnTextViewChanged
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(1, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var renderSize = RenderSize;
        var pen = _linePen; // Use the potentially theme-aware pen

        if (pen == null || renderSize.Width <= 0) return;

        double x = renderSize.Width / 2.0;
        x = Math.Floor(x) + 0.5;

        drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, renderSize.Height));
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        base.OnTextViewChanged(oldTextView, newTextView);
        // NEON: Update pen when TextView changes (theme might change)
        _linePen = CreateThemeAwarePen(newTextView);
        InvalidateVisual(); // Redraw with potentially new pen
    }
}
