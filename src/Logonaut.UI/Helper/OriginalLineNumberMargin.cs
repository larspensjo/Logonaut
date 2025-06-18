// ===== File: src\Logonaut.UI\Helpers\OriginalLineNumberMargin.cs =====
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common;

namespace Logonaut.UI.Helpers;

/*
 * A custom margin for the AvalonEdit editor that displays the original line number
 * for each log entry, as specified by the FilteredLogLine.OriginalLineNumber property.
 * This is crucial for maintaining context when logs are filtered.
 */
public class OriginalLineNumberMargin : AbstractMargin
{
    #region Dependency Properties

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<FilteredLogLine>),
            typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(null, OnVisualPropertyChanged));

    public IEnumerable<FilteredLogLine> ItemsSource
    {
        get => (IEnumerable<FilteredLogLine>)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    // DependencyProperty for binding the TextEditor instance.
    public static readonly DependencyProperty TextEditorProperty =
        DependencyProperty.Register(
            nameof(TextEditor),
            typeof(TextEditor),
            typeof(OriginalLineNumberMargin),
            new FrameworkPropertyMetadata(null, OnTextEditorChanged));

    public TextEditor TextEditor
    {
        get => (TextEditor)GetValue(TextEditorProperty);
        set => SetValue(TextEditorProperty, value);
    }

    public static readonly DependencyProperty ForegroundProperty =
        Control.ForegroundProperty.AddOwner(typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(OnVisualPropertyChanged));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly DependencyProperty FontFamilyProperty =
        Control.FontFamilyProperty.AddOwner(typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(OnVisualPropertyChanged));

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public static readonly DependencyProperty FontSizeProperty =
        Control.FontSizeProperty.AddOwner(typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(OnVisualPropertyChanged));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly DependencyProperty FontStyleProperty =
        Control.FontStyleProperty.AddOwner(typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(OnVisualPropertyChanged));

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public static readonly DependencyProperty FontWeightProperty =
        Control.FontWeightProperty.AddOwner(typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(OnVisualPropertyChanged));

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public static readonly DependencyProperty FontStretchProperty =
        Control.FontStretchProperty.AddOwner(typeof(OriginalLineNumberMargin), new FrameworkPropertyMetadata(OnVisualPropertyChanged));

    public FontStretch FontStretch
    {
        get => (FontStretch)GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    #endregion

    private IReadOnlyList<FilteredLogLine>? _currentLinesCache;

    private static void OnTextEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // When the TextEditor is bound, connect our margin to its TextView
        var margin = (OriginalLineNumberMargin)d;
        margin.TextView = (e.NewValue as TextEditor)?.TextArea.TextView;
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var margin = (OriginalLineNumberMargin)d;
        if (e.Property == ItemsSourceProperty)
        {
            margin.UpdateCache();
        }
        margin.InvalidateMeasure();
        margin.InvalidateVisual();
    }

    private void UpdateCache()
    {
        _currentLinesCache = ItemsSource as IReadOnlyList<FilteredLogLine> ?? ItemsSource?.ToList();
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
            newTextView.VisualLinesChanged += TextViewVisualLinesChanged;
            newTextView.ScrollOffsetChanged += TextViewScrollOffsetChanged;
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void TextViewScrollOffsetChanged(object? sender, EventArgs e) => InvalidateVisual();
    private void TextViewVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        var typeface = new Typeface(this.FontFamily, this.FontStyle, this.FontWeight, this.FontStretch);
        double emSize = this.FontSize;

        int maxLineNumber = 1;
        if (_currentLinesCache != null && _currentLinesCache.Any())
        {
            maxLineNumber = _currentLinesCache.Max(line => line.OriginalLineNumber);
        }

        int maxDigits = Math.Max(3, (int)Math.Log10(maxLineNumber) + 1);
        var widestNumberString = new string('9', maxDigits);

        var formattedNumber = new FormattedText(
            widestNumberString, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, emSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip
        );

        return new Size(formattedNumber.Width + 8, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        var renderSize = RenderSize;

        if (textView == null || !textView.VisualLinesValid || _currentLinesCache == null) return;

        var typeface = new Typeface(this.FontFamily, this.FontStyle, this.FontWeight, this.FontStretch);
        double emSize = this.FontSize;
        var foreground = this.Foreground;

        foreach (var visualLine in textView.VisualLines)
        {
            int documentLineNumber = visualLine.FirstDocumentLine.LineNumber;
            int lineIndex = documentLineNumber - 1;

            if (lineIndex >= 0 && lineIndex < _currentLinesCache.Count)
            {
                var filteredLine = _currentLinesCache[lineIndex];
                string numberString = filteredLine.OriginalLineNumber.ToString(CultureInfo.CurrentCulture);

                var formattedText = new FormattedText(
                    numberString, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, emSize, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip
                );

                double xPos = renderSize.Width - formattedText.Width - 4;
                double yPos = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.TextTop) - textView.VerticalOffset;

                if (yPos > -formattedText.Height && yPos < renderSize.Height)
                {
                    drawingContext.DrawText(formattedText, new Point(xPos, yPos));
                }
            }
        }
    }
}
