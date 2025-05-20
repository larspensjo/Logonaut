using System.Globalization;
using System.Windows;
using System.Windows.Controls; // Required for TextBlock
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common; // For FilteredLogLine
using System.Diagnostics; // For Debug

namespace Logonaut.UI.Helpers;

// Custom AvalonEdit margin to display original line numbers from FilteredLogLine data.
public class OriginalLineNumberMargin : AbstractMargin
{
    // DependencyProperty to bind the list of FilteredLogLine objects
    public static readonly DependencyProperty FilteredLinesSourceProperty =
        DependencyProperty.Register(nameof(FilteredLinesSource), typeof(IEnumerable<FilteredLogLine>),
            typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnFilteredLinesSourceChanged));

    public IEnumerable<FilteredLogLine> FilteredLinesSource
    {
        get => (IEnumerable<FilteredLogLine>)GetValue(FilteredLinesSourceProperty);
        set => SetValue(FilteredLinesSourceProperty, value);
    }

    private static void OnFilteredLinesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Invalidation is handled by AffectsMeasure/AffectsRender flags now
        (d as OriginalLineNumberMargin)?.UpdateCache(); // Update cache if source changes
    }

    private double _emSize;
    private Typeface _typeface = new Typeface("Consolas"); // Default typeface
    private double _lineNumberWidth;
    private IReadOnlyList<FilteredLogLine>? _currentLinesCache; // Cache the list for performance
    private int _maxDigits = 3; // Minimum number of digits to reserve space for

    // Updates the internal cache of lines - Call when FilteredLinesSource changes or needs refresh
    private void UpdateCache()
    {
        _currentLinesCache = FilteredLinesSource as IReadOnlyList<FilteredLogLine> ?? FilteredLinesSource?.ToList();
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= TextViewVisualLinesChanged;
            oldTextView.ScrollOffsetChanged -= TextViewScrollOffsetChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
        {
            var fontFamily = newTextView.GetValue(TextBlock.FontFamilyProperty) as FontFamily ?? new FontFamily("Consolas");
            var fontStyle = (FontStyle)newTextView.GetValue(TextBlock.FontStyleProperty);
            var fontWeight = (FontWeight)newTextView.GetValue(TextBlock.FontWeightProperty);
            var fontStretch = (FontStretch)newTextView.GetValue(TextBlock.FontStretchProperty); // Get FontStretch
            _typeface = new Typeface(fontFamily, fontStyle, fontWeight, fontStretch); // Use FontStretch
            _emSize = (double)newTextView.GetValue(TextBlock.FontSizeProperty);

            newTextView.VisualLinesChanged += TextViewVisualLinesChanged;
            newTextView.ScrollOffsetChanged += TextViewScrollOffsetChanged;
        }
        else
        {
            // Reset to defaults if TextView is removed
            _typeface = new Typeface("Consolas");
            _emSize = 12;
        }
        InvalidateMeasure(); // Re-calculate width
        InvalidateVisual();
    }

    /// <summary>
    /// Public method to explicitly refresh font properties from the TextView and update the margin.
    /// </summary>
    public void RefreshFontProperties()
    {
        if (TextView != null)
        {
            var fontFamily = TextView.GetValue(TextBlock.FontFamilyProperty) as FontFamily ?? new FontFamily("Consolas");
            var fontStyle = (FontStyle)TextView.GetValue(TextBlock.FontStyleProperty);
            var fontWeight = (FontWeight)TextView.GetValue(TextBlock.FontWeightProperty);
            var fontStretch = (FontStretch)TextView.GetValue(TextBlock.FontStretchProperty); // Get FontStretch
            _typeface = new Typeface(fontFamily, fontStyle, fontWeight, fontStretch); // Use FontStretch
            _emSize = (double)TextView.GetValue(TextBlock.FontSizeProperty);

            InvalidateMeasure();
            InvalidateVisual(); // Good practice to invalidate visual too
            Debug.WriteLine($"OriginalLineNumberMargin: Refreshed font properties. New Size: {_emSize}, Family: {fontFamily.Source}");
        }
        else
        {
            Debug.WriteLine("OriginalLineNumberMargin: RefreshFontProperties called, but TextView is null.");
        }
    }


    private void TextViewScrollOffsetChanged(object? sender, EventArgs e)
    {
        InvalidateVisual(); // Redraw on scroll
    }

    private void TextViewVisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateCache();

        int maxLineNumber = 1;
        if (_currentLinesCache != null && _currentLinesCache.Any())
        {
            maxLineNumber = _currentLinesCache.Max(line => line.OriginalLineNumber);
        }
        else if (TextView?.Document != null && TextView.Document.LineCount > 0)
        {
            maxLineNumber = TextView.Document.LineCount;
        }

        _maxDigits = Math.Max(3, (int)Math.Log10(maxLineNumber) + 1);
        var widestNumberString = new string('9', _maxDigits);

        var formattedNumber = new FormattedText(
            widestNumberString,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _emSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip
        );

        _lineNumberWidth = formattedNumber.WidthIncludingTrailingWhitespace;
        return new Size(_lineNumberWidth + 5, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        var renderSize = RenderSize;

        if (textView == null || !textView.VisualLinesValid || _currentLinesCache == null) return;

        var foreground = (Brush)textView.GetValue(Control.ForegroundProperty) ?? Brushes.Gray;

        foreach (var visualLine in textView.VisualLines)
        {
            int documentLineNumber = visualLine.FirstDocumentLine.LineNumber;
            int lineIndex = documentLineNumber - 1;

            if (lineIndex >= 0 && lineIndex < _currentLinesCache.Count)
            {
                var filteredLine = _currentLinesCache[lineIndex];
                int originalNumber = filteredLine.OriginalLineNumber;
                string numberString = originalNumber.ToString(CultureInfo.CurrentCulture);

                var formattedText = new FormattedText(
                    numberString,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    _typeface,
                    _emSize,
                    foreground,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip
                );

                double xPos = renderSize.Width - formattedText.WidthIncludingTrailingWhitespace - 2;
                double yPos = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;

                if (yPos >= 0 && yPos < renderSize.Height)
                {
                    drawingContext.DrawText(formattedText, new Point(xPos, yPos));
                }
            }
        }
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
    }
}
