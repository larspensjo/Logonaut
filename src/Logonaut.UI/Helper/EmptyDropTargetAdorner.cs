using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization; // Required for CultureInfo if using FormattedText

namespace Logonaut.UI.Helpers;

public class EmptyDropTargetAdorner : Adorner
{
    private readonly Brush _fillBrush;
    private readonly Pen _borderPen;
    private readonly FormattedText _formattedText;

    public EmptyDropTargetAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;

        Brush? fillBrushResource = null;
        Brush? borderBrushResource = null;
        Brush? textBrushResource = null;

        if (adornedElement is FrameworkElement fe) // Check if it's a FrameworkElement
        {
            fillBrushResource = fe.TryFindResource("PanelBackgroundBrush") as Brush;
            borderBrushResource = fe.TryFindResource("AccentBrush") as Brush;
            textBrushResource = fe.TryFindResource("SecondaryTextBrush") as Brush;
        }

        _fillBrush = fillBrushResource ?? new SolidColorBrush(Color.FromArgb(30, 128, 128, 128)); // Semi-transparent gray fallback
        
        var borderBrush = borderBrushResource ?? Brushes.DarkGray; // Fallback
        _borderPen = new Pen(borderBrush, 1.5) { DashStyle = DashStyles.Dash };

        var textBrush = textBrushResource ?? Brushes.Gray; // Fallback
        _formattedText = new FormattedText(
            "Drop here to create filter",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            14, 
            textBrush,
            VisualTreeHelper.GetDpi(adornedElement).PixelsPerDip 
        );
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        Rect adornedElementRect = new Rect(this.AdornedElement.RenderSize);

        // Slightly inset the rectangle
        double inset = 5;
        Rect renderRect = new Rect(
            adornedElementRect.Left + inset,
            adornedElementRect.Top + inset,
            adornedElementRect.Width - (2 * inset),
            adornedElementRect.Height - (2 * inset)
        );

        // Ensure width/height are not negative if the adorned element is very small
        if (renderRect.Width < 0) renderRect.Width = 0;
        if (renderRect.Height < 0) renderRect.Height = 0;

        drawingContext.DrawRectangle(_fillBrush, _borderPen, renderRect);

        // Calculate text position to center it
        Point textPosition = new Point(
            (adornedElementRect.Width - _formattedText.Width) / 2,
            (adornedElementRect.Height - _formattedText.Height) / 2
        );
            // Ensure text position is not negative
        if (textPosition.X < 0) textPosition.X = inset + 2;
        if (textPosition.Y < 0) textPosition.Y = inset + 2;


        drawingContext.DrawText(_formattedText, textPosition);
    }
}
