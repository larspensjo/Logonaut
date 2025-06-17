using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.Common;
using System.Diagnostics;
using System.Windows; // Added to resolve the 'Rect' type

namespace Logonaut.UI.Helpers;

/*
 * A background renderer for AvalonEdit that highlights a specific line in the document based on an index.
 * This is used to visually indicate the currently selected or active line, as determined
 * by user interaction (e.g., caret movement, clicking on a status bar element).
 *
 * It works in conjunction with the ViewModel's selection properties. When the selected
 * index changes in the ViewModel, this renderer is updated, and it applies a
 * background brush to the corresponding line in the text editor view.
 */
public class SelectedIndexHighlightTransformer : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;
    public Brush? HighlightBrush { get; set; }
    public int SelectedIndex { get; set; } = -1;
    
    // This property is required by TabView to link the transformer to the data source.
    public IReadOnlyList<FilteredLogLine>? FilteredLinesSource { get; set; }

    public SelectedIndexHighlightTransformer()
    {
        // Constructor can be empty
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (HighlightBrush == null || SelectedIndex < 0 || textView?.Document == null || SelectedIndex >= textView.Document.LineCount)
        {
            return;
        }

        // The IsDisposed check was removed as it's not a valid property on TextView.
        // The try-catch and null checks are sufficient.

        // AvalonEdit lines are 1-based, SelectedIndex is 0-based
        var line = textView.Document.GetLineByNumber(SelectedIndex + 1);
        if (line == null || line.IsDeleted)
        {
            return;
        }

        var segment = new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset };

        if (textView.GetVisualTopByDocumentLine(line.LineNumber) < textView.RenderSize.Height &&
            textView.GetVisualTopByDocumentLine(line.LineNumber) + textView.DefaultLineHeight > 0)
        {
            try
            {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment, true))
                {
                    // Draw a rectangle that spans the full width of the editor for the line.
                    drawingContext.DrawRectangle(HighlightBrush, null, new Rect(0, rect.Y, textView.ActualWidth, rect.Height));
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"SelectedIndexHighlightTransformer.Draw failed: {ex.Message}");
            }
        }
    }
}
