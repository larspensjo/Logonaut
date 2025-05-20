using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using Logonaut.UI.ViewModels;
using Logonaut.UI.Helpers;
using System.Linq; // Added for OfType()

namespace Logonaut.UI;

public partial class MainWindow : Window, IDisposable
{
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_chunkSeparator != null && (e.PropertyName == nameof(MainViewModel.FilteredLogLines) || e.PropertyName == nameof(MainViewModel.ContextLines)))
        {
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);
            if (_selectedIndexTransformer != null && _logOutputEditor.TextArea?.TextView != null)
            {
                _selectedIndexTransformer.FilteredLinesSource = _viewModel.FilteredLogLines;
                _logOutputEditor.TextArea.TextView.Redraw();
            }
        }

        if (_selectedIndexTransformer != null && _logOutputEditor.TextArea?.TextView != null)
        {
            if (e.PropertyName == nameof(MainViewModel.HighlightedOriginalLineNumber))
            {
                var highlightBrush = _logOutputEditor.TextArea.TextView.Tag as Brush;
                _selectedIndexTransformer.UpdateState(
                    _viewModel.HighlightedOriginalLineNumber,
                    highlightBrush,
                    _viewModel.FilteredLogLines,
                    _logOutputEditor.TextArea.TextView
                );
                Debug.WriteLine($"ViewModel_PropertyChanged: HighlightOriginalLineNumber changed to {_viewModel.HighlightedOriginalLineNumber}. Transformer updated.");
            }
            else if (e.PropertyName == nameof(MainViewModel.HighlightedFilteredLineIndex))
            {
                if (_viewModel.HighlightedFilteredLineIndex >= 0 && _viewModel.IsAutoScrollEnabled)
                {
                    bool highlightedLineIsLastLine = (_viewModel.FilteredLogLines.Count > 0 &&
                                                    _viewModel.HighlightedFilteredLineIndex == _viewModel.FilteredLogLines.Count - 1);
                    if (!highlightedLineIsLastLine)
                    {
                        _viewModel.IsAutoScrollEnabled = false;
                    }
                }
                Debug.WriteLine($"ViewModel_PropertyChanged: HighlightFilteredLineIndex changed to {_viewModel.HighlightedFilteredLineIndex}.");
            }
        }

        // Font Change Handling for Custom Margins AND EDITOR
        if (e.PropertyName == nameof(MainViewModel.EditorFontFamilyName) ||
            e.PropertyName == nameof(MainViewModel.EditorFontSize))
        {
            if (_logOutputEditor?.TextArea != null) // Ensure TextArea is available
            {
                // Directly update the TextArea's font properties
                if (e.PropertyName == nameof(MainViewModel.EditorFontFamilyName))
                {
                    if (_viewModel.EditorFontFamilyName != null) // Check for null before creating FontFamily
                    {
                        _logOutputEditor.TextArea.FontFamily = new FontFamily(_viewModel.EditorFontFamilyName);
                        Debug.WriteLine($"MainWindow: TextArea.FontFamily explicitly set to '{_viewModel.EditorFontFamilyName}'.");
                    }
                }
                else if (e.PropertyName == nameof(MainViewModel.EditorFontSize))
                {
                    _logOutputEditor.TextArea.FontSize = _viewModel.EditorFontSize;
                    Debug.WriteLine($"MainWindow: TextArea.FontSize explicitly set to '{_viewModel.EditorFontSize}'.");
                }

                // Update custom margins
                if (_logOutputEditor.TextArea.LeftMargins != null)
                {
                    foreach (var margin in _logOutputEditor.TextArea.LeftMargins.OfType<OriginalLineNumberMargin>())
                    {
                        margin.RefreshFontProperties();
                        Debug.WriteLine($"MainWindow: ViewModel font property '{e.PropertyName}' changed. Called RefreshFontProperties on OriginalLineNumberMargin.");
                    }
                    // If other margins become font-dependent, refresh them here too.
                }
            }
            else
            {
                Debug.WriteLine($"MainWindow: ViewModel font property '{e.PropertyName}' changed, but LogOutputEditor or its TextArea is null.");
            }
        }
    }

    private void ViewModel_RequestScrollToEnd(object? sender, EventArgs e)
    {
        if (_viewModel.IsAutoScrollEnabled)
        {
            _logOutputEditor?.ScrollToEnd();
        }
    }

    private void ViewModel_RequestScrollToLineIndex(object? sender, int lineIndex)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollToSelectedLine(lineIndex);
        }));
    }
}
