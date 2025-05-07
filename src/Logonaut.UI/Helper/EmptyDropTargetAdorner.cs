using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization; // Required for CultureInfo if using FormattedText

namespace Logonaut.UI.Helpers;

public class EmptyDropTargetAdorner : Adorner
{
    private Brush? _fillBrush;
    private Pen? _borderPen;
    private FormattedText? _formattedText;
    private bool _isActiveState; // To differentiate between resting and active drag-over

    // Brushes loaded once
    private readonly Brush? _activeFillBrush;
    private readonly Brush? _activeBorderBrush;
    private readonly Brush? _activeTextBrush;
    private readonly Brush? _restingFillBrush;
    private readonly Brush? _restingBorderBrush;
    private readonly Brush? _restingTextBrush;
    private readonly Typeface _typeface;


    public EmptyDropTargetAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        if (adornedElement is not FrameworkElement fe)
            throw new ArgumentException("Adorned element must be a FrameworkElement.", nameof(adornedElement)); 

        _activeFillBrush = fe.TryFindResource("EmptyDropAdorner.BackgroundBrush") as Brush;
        _activeBorderBrush = fe.TryFindResource("EmptyDropAdorner.BorderBrush") as Brush;
        _activeTextBrush = fe.TryFindResource("EmptyDropAdorner.TextBrush") as Brush;

        _restingFillBrush = fe.TryFindResource("EmptyDropAdorner.Resting.BackgroundBrush") as Brush;
        _restingBorderBrush = fe.TryFindResource("EmptyDropAdorner.Resting.BorderBrush") as Brush;
        _restingTextBrush = fe.TryFindResource("EmptyDropAdorner.Resting.TextBrush") as Brush;

        // Initialize with resting state visuals by default
        SetVisualState(false); 
    }

    public void SetVisualState(bool isActive)
    {
        if (_isActiveState == isActive && _fillBrush != null) // Avoid unnecessary re-creation if state hasn't changed
            return;

        _isActiveState = isActive;

        Brush? fillBrushSource = isActive ? _activeFillBrush : _restingFillBrush;
        Brush? borderBrushSource = isActive ? _activeBorderBrush : _restingBorderBrush;
        Brush? textBrushSource = isActive ? _activeTextBrush : _restingTextBrush;
        DashStyle dashStyle = isActive ? DashStyles.Dash : DashStyles.Dot; // Example: Dot for resting, Dash for active

        _fillBrush = fillBrushSource ?? new SolidColorBrush(isActive ? Color.FromArgb(30, 128, 128, 128) : Color.FromArgb(15, 128, 128, 128));
        
        var finalBorderBrush = borderBrushSource ?? (isActive ? Brushes.DarkGray : Brushes.LightGray);
        _borderPen = new Pen(finalBorderBrush, isActive ? 1.5 : 1.0) { DashStyle = dashStyle };

        var finalTextBrush = textBrushSource ?? (isActive ? Brushes.Gray : Brushes.LightGray);
        _formattedText = new FormattedText(
            isActive ? "Drop here to create filter" : "Drag filter here", // Different text for states
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            _typeface, // Use the cached typeface
            isActive ? 14 : 12, // Slightly smaller font for resting state
            finalTextBrush,
            VisualTreeHelper.GetDpi(this.AdornedElement).PixelsPerDip
        );

        InvalidateVisual(); // Request a re-render
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_formattedText is null)
            throw new InvalidOperationException("_formattedText is not initialized.");

        Rect adornedElementRect = new Rect(this.AdornedElement.RenderSize);
        double inset = 5;
        Rect renderRect = new Rect(
            adornedElementRect.Left + inset,
            adornedElementRect.Top + inset,
            adornedElementRect.Width - (2 * inset),
            adornedElementRect.Height - (2 * inset)
        );

        if (renderRect.Width < 0) renderRect.Width = 0;
        if (renderRect.Height < 0) renderRect.Height = 0;

        drawingContext.DrawRectangle(_fillBrush, _borderPen, renderRect);

        Point textPosition = new Point(
            (adornedElementRect.Width - _formattedText.Width) / 2,
            (adornedElementRect.Height - _formattedText.Height) / 2
        );
        if (textPosition.X < 0) textPosition.X = inset + 2;
        if (textPosition.Y < 0) textPosition.Y = inset + 2;

        drawingContext.DrawText(_formattedText, textPosition);
    }
}
