using System.Windows;
using System.Windows.Data; // Required for Binding
using System.Windows.Input; // Required for RoutedUICommand
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Editing; // Required for Caret
using ICSharpCode.AvalonEdit.Document; // Required for DocumentLine
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;
using System.Diagnostics;

namespace Logonaut.UI;

// Event handlers and setup specifically for interacting with the AvalonEdit LogOutputEditor.
// This groups all the direct setup, event handling, and custom rendering logic associated with the AvalonEdit control.
public partial class MainWindow : Window, IDisposable
{
    private int _lastKnownCaretLine = -1; // Field to track the last caret line
    private OverviewRulerMargin? _overviewRuler;
    private ChunkSeparatorRenderer? _chunkSeparator;
    private SelectedIndexHighlightTransformer? _selectedIndexTransformer;

    // Only for things not dependent on ViewModel knowing the editor
    private void LogOutputEditor_Loaded(object sender, RoutedEventArgs e)
    {
        // The template should be applied now, try to find the ruler
        // Use VisualTreeHelper to find the element within the template
        _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(_logOutputEditor);

        if (_overviewRuler != null)
            _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
        else
            throw new Exception("OverviewRulerMargin not found in TextEditor template.");

        // Unsubscribe when the editor unloads to prevent memory leaks
        _logOutputEditor.Unloaded += (s, ev) =>
        {
            if (_overviewRuler != null)
            {
                _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
            }
            // Also unsubscribe from Loaded/Unloaded? Might not be necessary if window closes.
        };
    }

    private void LogOutputEditor_Unloaded(object? sender, RoutedEventArgs? e)
    {
        if (_logOutputEditor?.TextArea != null)
            _logOutputEditor.TextArea.SelectionChanged -= LogOutputEditor_SelectionChanged;

        // Clean up Overview Ruler binding
        if (_overviewRuler != null)
        {
            _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
            _overviewRuler.PreviewMouseLeftButtonDown -= OverviewRuler_PreviewMouseLeftButtonDown;
        }
        _overviewRuler = null; // Release reference

        if (_logOutputEditor?.TextArea?.Caret != null)
            _logOutputEditor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;

        // --- Unsubscribe from User Input Events ---
        if (_logOutputEditor?.TextArea != null)
        {
            _logOutputEditor.TextArea.PreviewMouseWheel -= TextArea_PreviewMouseWheel;
            _logOutputEditor.TextArea.PreviewKeyDown -= TextArea_PreviewKeyDown;
        }

        TextView? textView = _logOutputEditor?.TextArea?.TextView;
        if (textView == null && _logOutputEditor != null) // Check _logOutputEditor as well because TextArea might be null if unloaded very early
        {
            // If textView is null during unload, it's possible the control is already significantly torn down.
            // Log a warning, but try to proceed with other cleanup.
            Debug.WriteLine("WARN: TextView was null during LogOutputEditor_Unloaded. Some cleanup might be skipped.");
        }


        if (_selectedIndexTransformer != null && textView != null)
        {
            if (textView.LineTransformers.Contains(_selectedIndexTransformer))
            {
                textView.LineTransformers.Remove(_selectedIndexTransformer);
            }
            // Clear the proxy property used for the brush
            textView.ClearValue(TextView.TagProperty);
        }
        _selectedIndexTransformer = null;

        // Clean up Chunk Separator
        if (_chunkSeparator != null && textView != null)
        {
            // Clear bindings
            BindingOperations.ClearBinding(_chunkSeparator, ChunkSeparatorRenderer.SeparatorBrushProperty);
            textView.ClearValue(TextView.TagProperty); // Clear the resource reference on Tag

            // Remove renderer and dispose
            if (textView.BackgroundRenderers.Contains(_chunkSeparator))
            {
                textView.BackgroundRenderers.Remove(_chunkSeparator);
            }
            _chunkSeparator.Dispose();
        }
        _chunkSeparator = null; // Release reference
    }

    private void LogOutputEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+V Paste Logic
        if (e.Key == System.Windows.Input.Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                string clipboardText = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    _viewModel.LoadLogFromText(clipboardText);
                    e.Handled = true;
                    return; // Handled
                }
            }
        }

        // Ctrl+F3 Search Selected Text
        if (e.Key == System.Windows.Input.Key.F3 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Check if there is selected text in the editor
            if (_logOutputEditor.SelectionLength > 0 && !string.IsNullOrEmpty(_logOutputEditor.SelectedText))
            {
                string selected = _logOutputEditor.SelectedText;

                // Update the ViewModel's SearchText
                _viewModel.SearchText = selected;

                // Trigger "Find Next" immediately
                // (Setting SearchText likely triggers CanExecuteChanged if needed)
                // TODO: Should start search from current position, not from the beginning.
                if (_viewModel.NextSearchCommand.CanExecute(null))
                {
                    _viewModel.NextSearchCommand.Execute(null); // The first search will find the string currently selected
                    _viewModel.NextSearchCommand.Execute(null);
                }

                e.Handled = true; // Mark as handled
            }
            // If no text is selected, Ctrl+F3 currently does nothing in the editor.
            // You could potentially add fallback behavior here if desired.
            return; // Handled or nothing to do for Ctrl+F3
        }

        // Note: F3 and Shift+F3 are now handled by the Window.InputBindings
        //       because they should work globally, not just when the editor has focus.
        //       If we handled them here, they wouldn't work when e.g. the search box has focus.
    }

    private void LogOutputEditor_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left && DataContext is ViewModels.MainViewModel viewModel)
        {
            var textView = _logOutputEditor.TextArea.TextView;
            if (!textView.IsLoaded || !textView.VisualLinesValid)
                throw new InvalidOperationException("TextView is not loaded or visual lines are not valid.");

            Point pointRelativeToTextView = e.GetPosition(textView);
            Point pointInDocument = new Point(
                pointRelativeToTextView.X + textView.ScrollOffset.X,
                pointRelativeToTextView.Y + textView.ScrollOffset.Y
            );
            TextViewPosition? positionInfo = textView.GetPosition(pointInDocument);

            int clickedOriginalLineNumber = -1; // Default to -1 if no line is clicked
            if (positionInfo.HasValue)
            {
                VisualLine? clickedVisualLine = textView.GetVisualLine(positionInfo.Value.Line);
                if (clickedVisualLine != null)
                {
                    int clickedDocumentLineNumber = clickedVisualLine.FirstDocumentLine.LineNumber; // 1-based in current view
                    int clickedFilteredLineIndex = clickedDocumentLineNumber - 1;

                    if (clickedFilteredLineIndex >= 0 && clickedFilteredLineIndex < viewModel.FilteredLogLines.Count)
                    {
                        clickedOriginalLineNumber = viewModel.FilteredLogLines[clickedFilteredLineIndex].OriginalLineNumber;
                    }
                }
            }

            // Update the highlighted original line number in the ViewModel
            viewModel.HighlightedOriginalLineNumber = clickedOriginalLineNumber;

            // Explicitly disable auto-scroll upon any left mouse click that results in a selection
            if (clickedOriginalLineNumber >= 0 && viewModel.IsAutoScrollEnabled)
            {
                viewModel.IsAutoScrollEnabled = false;
            }
        }
    }

    private void LogOutputEditor_SelectionChanged(object? sender, EventArgs e) // Hook this up to editor.TextArea.SelectionChanged
    {
        if (DataContext is MainViewModel viewModel && sender is TextArea textArea)
        {
            string selectedText = textArea.Selection.GetText();
            // Check for multi-line. SegmentTree.GetText() gives raw text.
            if (!string.IsNullOrEmpty(selectedText) && (selectedText.Contains('\r') || selectedText.Contains('\n')))
            {
                viewModel.SelectedLogTextForFilter = null; // Multi-line, invalidate
            }
            else
            {
                viewModel.SelectedLogTextForFilter = string.IsNullOrEmpty(selectedText) ? null : selectedText;
            }
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (sender is Caret caret && DataContext is MainViewModel viewModel)
        {
            int currentCaretLine = caret.Line; // 1-based line number

            if (currentCaretLine != _lastKnownCaretLine) // Process only if line actually changed
            {
                _lastKnownCaretLine = currentCaretLine;
                int filteredLineIndex = currentCaretLine - 1;

                // Update the ViewModel's highlighted line index if it's a valid line
                // This will trigger ViewModel_PropertyChanged if the value actually changes.
                if (filteredLineIndex >= 0 && filteredLineIndex < viewModel.FilteredLogLines.Count)
                {
                    // Set HighlightedOriginalLineNumber instead of HighlightedFilteredLineIndex
                    // to make the selection "stick" to the original content.
                    viewModel.HighlightedOriginalLineNumber = viewModel.FilteredLogLines[filteredLineIndex].OriginalLineNumber;
                }
                else
                {
                    if (viewModel.HighlightedOriginalLineNumber != -1)
                        viewModel.HighlightedOriginalLineNumber = -1;
                }
            }
        }
    }

    private void TextArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Disable auto-scroll if scrolling UP (positive delta)
        if (_viewModel.IsAutoScrollEnabled && e.Delta > 0)
        {
            _viewModel.IsAutoScrollEnabled = false;
            // System.Diagnostics.Debug.WriteLine("AutoScroll disabled due to MouseWheel Up.");
        }
    }

    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsAutoScrollEnabled) return; // Only act if enabled

        switch (e.Key)
        {
            case Key.PageUp:
            case Key.PageDown:
            case Key.Up: // Arrow keys might not always scroll, but disabling is safe
            case Key.Down:
            case Key.Home: // Home/End usually scroll vertically
            case Key.End:
                _viewModel.IsAutoScrollEnabled = false;
                // System.Diagnostics.Debug.WriteLine($"AutoScroll disabled due to KeyDown: {e.Key}");
                // Note: We don't set e.Handled = true here unless we want to
                // completely prevent the default scroll action for some reason.
                break;
        }
    }

    // Handler for the ruler's request to scroll
    private void OverviewRuler_RequestScrollOffset(object? sender, double requestedOffset)
    {
        _logOutputEditor.ScrollToVerticalOffset(requestedOffset);
    }

    private void OverviewRuler_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Disable auto-scroll when user clicks the ruler to start scrolling
        if (_viewModel.IsAutoScrollEnabled)
        {
            _viewModel.IsAutoScrollEnabled = false;
            // System.Diagnostics.Debug.WriteLine("AutoScroll disabled due to Ruler Click.");
        }
        // Allow the base class or other handlers to continue processing the click for actual scrolling
    }

    private void SetupCustomMargins()
    {
        var numberMargin = new Logonaut.UI.Helpers.OriginalLineNumberMargin();
        _logOutputEditor.TextArea.LeftMargins.Add(numberMargin);

        var filteredLinesBinding = new System.Windows.Data.Binding("FilteredLogLines")
        {
            Source = this.DataContext,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        numberMargin.SetBinding(Logonaut.UI.Helpers.OriginalLineNumberMargin.FilteredLinesSourceProperty, filteredLinesBinding);

        var visibilityBinding = new System.Windows.Data.Binding("IsCustomLineNumberMarginVisible")
        {
            Source = this.DataContext,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        numberMargin.SetBinding(UIElement.VisibilityProperty, visibilityBinding);

        // --- Separator Margin ---
        var lineSeparatorMargin = new Logonaut.UI.Helpers.VerticalLineMargin();
        lineSeparatorMargin.SetBinding(UIElement.VisibilityProperty, visibilityBinding);
        _logOutputEditor.TextArea.LeftMargins.Add(lineSeparatorMargin);
    }

    private void ScrollToSelectedLine(int filteredLineIndex)
    {
        if (_logOutputEditor?.Document == null)
            throw new InvalidOperationException("LogOutputEditor Document is null.");

        if (_logOutputEditor.TextArea?.TextView == null)
            throw new InvalidOperationException("LogOutputEditor TextView is null.");

        // Convert 0-based index to 1-based document line number
        int targetDocumentLineNumber = filteredLineIndex + 1;

        // Check if the target line number is valid within the *current* document content
        if (targetDocumentLineNumber <= 0 || targetDocumentLineNumber > _logOutputEditor.Document.LineCount)
        {
            System.Diagnostics.Debug.WriteLine($"ScrollToSelectedLine: Invalid target line number {targetDocumentLineNumber} for document length {_logOutputEditor.Document.LineCount}.");
            return;
        }

        try
        {
            var textView = _logOutputEditor.TextArea.TextView;
            DocumentLine targetLine = _logOutputEditor.Document.GetLineByNumber(targetDocumentLineNumber);

            // Calculate the visual top position of the line relative to the document start
            double visualTop = textView.GetVisualTopByDocumentLine(targetDocumentLineNumber);

            if (!double.IsNaN(visualTop) && !double.IsInfinity(visualTop))
            {
                // Calculate desired offset to center the line
                double desiredOffset = visualTop - (textView.ActualHeight / 2.0);

                // Use TextView.DocumentHeight (total content height) and TextView.ActualHeight (visible area height)
                double maxOffset = Math.Max(0, textView.DocumentHeight - textView.ActualHeight);

                double clampedOffset = Math.Max(0, Math.Min(desiredOffset, maxOffset));

                // Perform the scroll using the calculated vertical offset
                _logOutputEditor.ScrollToVerticalOffset(clampedOffset);
            }
            else
            {
                // Fallback to simple ScrollToLine if visual position calculation fails
                System.Diagnostics.Debug.WriteLine($"ScrollToSelectedLine: VisualTop calculation failed for line {targetDocumentLineNumber}. Falling back to ScrollToLine.");
                _logOutputEditor.ScrollToLine(targetDocumentLineNumber);
            }
        }
        catch (ArgumentOutOfRangeException argEx)
        {
            System.Diagnostics.Debug.WriteLine($"Error during ScrollToSelectedLine (ArgumentOutOfRange): {argEx.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during ScrollToSelectedLine: {ex.Message}");
        }

    }
}
