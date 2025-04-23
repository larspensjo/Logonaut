using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Logonaut.UI.ViewModels; // For SearchResult

namespace Logonaut.UI.Helpers;

public partial class OverviewRulerMargin : Control
{
    private bool _isDragging = false;

    // Event to notify the host (MainWindow) to scroll
    public event EventHandler<double>? RequestScrollOffset;

    #region Dependency Properties

    // --- Scroll Information ---
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ViewportHeight
    {
        get => (double)GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    public static readonly DependencyProperty ExtentHeightProperty =
        DependencyProperty.Register(nameof(ExtentHeight), typeof(double), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ExtentHeight
    {
        get => (double)GetValue(ExtentHeightProperty);
        set => SetValue(ExtentHeightProperty, value);
    }

    // --- Document Information ---
    public static readonly DependencyProperty DocumentLengthProperty =
        DependencyProperty.Register(nameof(DocumentLength), typeof(int), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int DocumentLength
    {
        get => (int)GetValue(DocumentLengthProperty);
        set => SetValue(DocumentLengthProperty, value);
    }

    // --- Markers ---
    public static readonly DependencyProperty SearchMarkersProperty =
        DependencyProperty.Register(nameof(SearchMarkers), typeof(IEnumerable<SearchResult>), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<SearchResult> SearchMarkers
    {
        get => (IEnumerable<SearchResult>)GetValue(SearchMarkersProperty);
        set => SetValue(SearchMarkersProperty, value);
    }

    // --- Appearance ---
    public static readonly DependencyProperty SearchMarkerBrushProperty =
        DependencyProperty.Register(nameof(SearchMarkerBrush), typeof(Brush), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(Brushes.DarkCyan, FrameworkPropertyMetadataOptions.AffectsRender)); // Example default

    public Brush SearchMarkerBrush
    {
        get => (Brush)GetValue(SearchMarkerBrushProperty);
        set => SetValue(SearchMarkerBrushProperty, value);
    }

    public static readonly DependencyProperty ThumbBrushProperty =
        DependencyProperty.Register(nameof(ThumbBrush), typeof(Brush), typeof(OverviewRulerMargin),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(100, 128, 128, 128)), FrameworkPropertyMetadataOptions.AffectsRender)); // Semi-transparent gray

    public Brush ThumbBrush
    {
        get => (Brush)GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }


    // Optional: Reference to TextView if needed for more complex mapping later
    // public static readonly DependencyProperty TextViewProperty = ...
    // public TextView TextView { get => ...; set => ...; }

    #endregion

    public OverviewRulerMargin()
    {
        InitializeComponent();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double extent = ExtentHeight;
        double viewport = ViewportHeight;
        double offset = VerticalOffset;
        int docLength = DocumentLength;
        double controlHeight = ActualHeight;

        // Keep the initial check
        if (controlHeight <= 0)
        {
            return; // Cannot draw if control has no height
        }

        double thumbHeight;
        double thumbTop;

        // Check if scrolling is possible / needed
        if (extent > viewport && viewport > 0) // Only calculate proportions if scrolling is active
        {
            // Calculate proportional height
            double proportionalHeight = (viewport / extent) * controlHeight;

            // Ensure minimum height BUT ALSO cap at control height
            thumbHeight = Math.Min(controlHeight, Math.Max(5, proportionalHeight)); // Cap at controlHeight

            // Calculate proportional top position
            thumbTop = (offset / extent) * controlHeight;

            // Ensure thumb doesn't go out of bounds due to calculation nuances
            thumbTop = Math.Max(0, Math.Min(thumbTop, controlHeight - thumbHeight));
        }
        else // All content fits, or viewport is invalid - thumb fills the whole track
        {
            thumbHeight = controlHeight; // Fill the entire ruler
            thumbTop = 0; // Thumb starts at the top
            // No need for Math.Max(5, ...) here because if controlHeight < 5, something else is wrong.
        }

        // Draw the (now correctly sized) thumb
        drawingContext.DrawRectangle(ThumbBrush, null, new Rect(0, thumbTop, ActualWidth, thumbHeight));

        // --- Draw Search Markers ---
        // Marker drawing logic remains the same, but needs the check for docLength > 0
        var markers = SearchMarkers;
        double markerWidth = ActualWidth/3.0; // Width of the marker rectangle. TOOD: When extending to custom markers, this may need to be changed.
        if (markers != null && docLength > 0 && extent > 0) // Check extent > 0 for scaling marker position too
        {
            foreach (var marker in markers)
            {
                // Calculate relative position based on document offset vs length
                // (Using document length is generally better for markers than scroll extent)
                double relativeDocPos = (double)marker.Offset / docLength;

                // Map to Y coordinate on the ruler
                double yPos = relativeDocPos * controlHeight;

                // Prevent drawing markers exactly at the bottom edge if scaling results in yPos == controlHeight
                yPos = Math.Min(yPos, controlHeight - 1.5); // Ensure marker fits within bounds

                // Draw a small rectangle for the marker
                drawingContext.DrawRectangle(SearchMarkerBrush, null, new Rect(0, yPos, markerWidth, 1.5));
            }
        }
        // else: No markers to draw or cannot scale them
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ExtentHeight > 0 && ActualHeight > 0)
        {
            e.Handled = true;
            CaptureMouse();
            _isDragging = true;
            ScrollToPosition(e.GetPosition(this).Y);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging)
        {
            ScrollToPosition(e.GetPosition(this).Y);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ScrollToPosition(double y)
    {
        if (ExtentHeight <= 0 || ActualHeight <= 0) return;

        // Calculate the desired offset based on the click position relative to the ruler height
        // Subtract half the viewport height to center the clicked position, approximately
        double desiredOffset = (y / ActualHeight) * ExtentHeight - (ViewportHeight / 2);

        // Clamp the offset within valid bounds
        desiredOffset = Math.Max(0, Math.Min(desiredOffset, ExtentHeight - ViewportHeight));

        // Raise the event to request scrolling
        RequestScrollOffset?.Invoke(this, desiredOffset);
    }
}
