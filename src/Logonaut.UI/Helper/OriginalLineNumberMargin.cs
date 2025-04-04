using System.Globalization;
using System.Windows;
using System.Windows.Controls; // Required for TextBlock
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common; // For FilteredLogLine

namespace Logonaut.UI.Helpers
{
    /// <summary>
    /// Custom AvalonEdit margin to display original line numbers from FilteredLogLine data.
    /// </summary>
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
            // (d as OriginalLineNumberMargin)?.UpdateCache(); // Update cache if source changes
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
                // Use TryFindResource to get editor font settings if possible
                var fontFamily = newTextView.GetValue(TextBlock.FontFamilyProperty) as FontFamily ?? new FontFamily("Consolas");
                var fontStyle = (FontStyle)newTextView.GetValue(TextBlock.FontStyleProperty);
                var fontWeight = (FontWeight)newTextView.GetValue(TextBlock.FontWeightProperty);
                _typeface = new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
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

        private void TextViewScrollOffsetChanged(object? sender, EventArgs e)
        {
            InvalidateVisual(); // Redraw on scroll
        }

        private void TextViewVisualLinesChanged(object? sender, EventArgs e)
        {
            // Only invalidate measure if the line count potentially changes max digits
            // For now, always invalidate measure and visual for simplicity
             InvalidateMeasure();
            InvalidateVisual();
        }

        // Calculate the required width for the margin
        protected override Size MeasureOverride(Size availableSize)
        {
            UpdateCache(); // Ensure cache is up-to-date

            int maxLineNumber = 1;
            if (_currentLinesCache != null && _currentLinesCache.Any())
            {
                maxLineNumber = _currentLinesCache.Max(line => line.OriginalLineNumber);
            }
            else if (TextView?.Document != null && TextView.Document.LineCount > 0)
            {
                 // Fallback: estimate based on document line count if no filtered lines yet
                 // This provides a better initial width guess
                 maxLineNumber = TextView.Document.LineCount;
            }

            // Determine the number of digits needed, ensuring at least _maxDigits
            _maxDigits = Math.Max(3, (int)Math.Log10(maxLineNumber) + 1);

            // Create a dummy string with the required number of '9's for width calculation
            var widestNumberString = new string('9', _maxDigits);

            // Format the dummy string to measure its width
             var formattedNumber = new FormattedText(
                 widestNumberString,
                 CultureInfo.CurrentCulture,
                 FlowDirection.LeftToRight,
                 _typeface,
                 _emSize,
                 Brushes.Black, // Color doesn't matter for measurement
                 VisualTreeHelper.GetDpi(this).PixelsPerDip
             );

            _lineNumberWidth = formattedNumber.WidthIncludingTrailingWhitespace;
            // Add some padding (e.g., 5 pixels)
            return new Size(_lineNumberWidth + 5, 0); // Width + padding, height is determined by TextView
        }

        // Render the line numbers
        protected override void OnRender(DrawingContext drawingContext)
        {
            var textView = TextView;
            var renderSize = RenderSize; // Use RenderSize for clipping bounds

            // Draw background if needed (e.g., to match editor theme)
             // drawingContext.DrawRectangle(TextView.Background, null, new Rect(0, 0, renderSize.Width, renderSize.Height));

            if (textView == null || !textView.VisualLinesValid || _currentLinesCache == null) return;

            // Use the foreground color from the TextView for consistency
            var foreground = (Brush)textView.GetValue(Control.ForegroundProperty) ?? Brushes.Gray;

            foreach (var visualLine in textView.VisualLines)
            {
                // Get the document line number (1-based) corresponding to this visual line
                // This is the line number within the *filtered* document being displayed.
                int documentLineNumber = visualLine.FirstDocumentLine.LineNumber;
                int lineIndex = documentLineNumber - 1; // Convert to 0-based index for list access

                if (lineIndex >= 0 && lineIndex < _currentLinesCache.Count)
                {
                    // Get the FilteredLogLine for this displayed line index
                    var filteredLine = _currentLinesCache[lineIndex];
                    int originalNumber = filteredLine.OriginalLineNumber;

                    // Format the original line number string
                    string numberString = originalNumber.ToString(CultureInfo.CurrentCulture);

                    // Create FormattedText for drawing
                     var formattedText = new FormattedText(
                         numberString,
                         CultureInfo.CurrentCulture,
                         FlowDirection.LeftToRight,
                         _typeface,
                         _emSize,
                         foreground,
                         VisualTreeHelper.GetDpi(this).PixelsPerDip
                     );

                    // Calculate drawing position (right-aligned)
                    double xPos = renderSize.Width - formattedText.WidthIncludingTrailingWhitespace - 2; // Subtract padding (adjust as needed)
                    // Get Y position relative to the margin's top
                    double yPos = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;

                    // Ensure the text is drawn within the margin's bounds
                    if (yPos >= 0 && yPos < renderSize.Height)
                    {
                        drawingContext.DrawText(formattedText, new Point(xPos, yPos));
                    }
                }
                // else: Should not happen if _currentLinesCache matches the displayed document lines
            }
        }

        // Optional: Handle mouse events if needed (e.g., for breakpoints)
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            // Example: Hit-testing logic could go here
            // var textView = this.TextView;
            // if (textView != null) { ... }
        }
    }
}
