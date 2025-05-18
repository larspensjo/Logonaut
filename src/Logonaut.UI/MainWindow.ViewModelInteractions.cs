using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI;

// Event handlers that respond to changes or requests from the MainViewModel
// This isolates the code that directly reacts to ViewModel events, keeping the "View reacting to ViewModel" logic together.
public partial class MainWindow : Window, IDisposable
{

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_chunkSeparator != null && (e.PropertyName == nameof(MainViewModel.FilteredLogLines) || e.PropertyName == nameof(MainViewModel.ContextLines)))
        {
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);
            // When FilteredLogLines changes, we also need to update the transformer's source
            if (_selectedIndexTransformer != null && _logOutputEditor.TextArea?.TextView != null)
            {
                _selectedIndexTransformer.FilteredLinesSource = _viewModel.FilteredLogLines;
                // Force a redraw if the highlight might need to be reapplied due to content change
                _logOutputEditor.TextArea.TextView.Redraw();
            }
        }


        if (_selectedIndexTransformer != null && _logOutputEditor.TextArea?.TextView != null)
        {
            // Handle HighlightedOriginalLineNumber change from ViewModel
            if (e.PropertyName == nameof(MainViewModel.HighlightedOriginalLineNumber))
            {
                var highlightBrush = _logOutputEditor.TextArea.TextView.Tag as Brush; 
                // Pass the current FilteredLogLines to the transformer.
                // It's important this is the same collection instance the editor is displaying.
                _selectedIndexTransformer.UpdateState(
                    _viewModel.HighlightedOriginalLineNumber, // Get the current value from MainViewModel (which delegates to TabViewModel)
                    highlightBrush,
                    _viewModel.FilteredLogLines, 
                    _logOutputEditor.TextArea.TextView
                );
                Debug.WriteLine($"ViewModel_PropertyChanged: HighlightOriginalLineNumber changed to {_viewModel.HighlightedOriginalLineNumber}. Transformer updated.");
            }
            // The HighlightedFilteredLineIndex property change handling remains for auto-scroll logic,
            // but the actual highlighting is now driven by HighlightedOriginalLineNumber.
            else if (e.PropertyName == nameof(MainViewModel.HighlightedFilteredLineIndex))
            {
                // Auto-scroll logic (remains relevant for when selection is driven by, e.g., search results)
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

        // Clear Status Message on Successful Jump ---
        if (e.PropertyName == nameof(MainViewModel.HighlightedOriginalLineNumber) &&
            !string.IsNullOrEmpty(_viewModel.JumpStatusMessage) &&
            _viewModel.HighlightedOriginalLineNumber.ToString() == _viewModel.TargetOriginalLineNumberInput)
        {
            // _viewModel.JumpStatusMessage = string.Empty; // <<< THIS IS THE ERROR. REMOVE IT.
            // Instead, TabViewModel should clear its own JumpStatusMessage when appropriate.
            // For now, we can't directly tell TabViewModel to clear it from here without adding more events/methods.
            // This logic might need to move into TabViewModel.
        }
    }

    private void ViewModel_RequestScrollToEnd(object? sender, EventArgs e)
    {
        // Check the ViewModel's IsAutoScrollEnabled property before scrolling
        if (_viewModel.IsAutoScrollEnabled)
        {
            _logOutputEditor?.ScrollToEnd();
        }
    }

    private void ViewModel_RequestScrollToLineIndex(object? sender, int lineIndex)
    {
        // Ensure this runs on the UI thread if there's any doubt
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollToSelectedLine(lineIndex);
        }));
    }
}
