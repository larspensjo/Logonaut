using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI.Views;

/*
 * The TabView user control encapsulates the display and interaction logic for a single log tab.
 * Its DataContext is expected to be a TabViewModel. This control is responsible for hosting the
 * AvalonEdit text editor, handling its setup, and bridging communication between the editor's
 * UI events and the corresponding TabViewModel.
 *
 * It manages editor-specific features like line highlighting, chunk separators for context lines,
 * and user interactions such as scrolling and line selection. By containing this logic, it allows
 * the MainWindow to simply host a collection of these views in a TabControl without needing to
 * know the details of log rendering.
 */
public partial class TabView : UserControl, IDisposable
{
    private TabViewModel? _viewModel;
    private TextEditor? _logOutputEditor;
    private SelectedIndexHighlightTransformer? _selectedIndexTransformer;
    private ChunkSeparatorRenderer? _chunkSeparator;
    private OverviewRulerMargin? _overviewRuler;
    private bool _disposed;
    private int _lastKnownCaretLine;

    public TabView()
    {
        Debug.WriteLine("--> TabView: Initializing TabView.");
        InitializeComponent();
        Loaded += TabView_Loaded;
        Unloaded += TabView_Unloaded;
        Debug.WriteLine("--> TabView: Initialized TabView.");
    }

    // Remove some AvalonEdit's default command bindings
    // This allows the command to bubble up to the MainWindow, where we have a global handler.
    // The bindings are on the internal TextArea, not the top-level TextEditor control.
    private void RemoveAvalonCommandBindings()
    {
        if (_logOutputEditor is null)
            return; // Will not happen, but just to satisfy the compiler.
        var pasteBinding = _logOutputEditor.TextArea.CommandBindings
            .OfType<CommandBinding>()
            .FirstOrDefault(cb => cb.Command == ApplicationCommands.Paste);

        if (pasteBinding != null)
        {
            _logOutputEditor.TextArea.CommandBindings.Remove(pasteBinding);
            Debug.WriteLine("--> TabView: Successfully removed default Paste command binding from AvalonEdit.TextArea.");
        }
        else
        {
            Debug.WriteLine("--> TabView: Could not find the default Paste command binding on AvalonEdit.TextArea to remove it.");
        }        
    }

    /*
     * Handles the setup logic when the TabView is loaded and added to the visual tree.
     * This is the primary point where the View connects with its ViewModel (TabViewModel). It finds the
     * necessary controls within its template (like the TextEditor), passes the editor instance to the
     * ViewModel, and subscribes to ViewModel events for UI updates like scrolling.
     */
    private void TabView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TabViewModel vm)
        {
            Debug.WriteLine("!!! TabView_Loaded: DataContext is not a TabViewModel. Aborting setup.");
            return;
        }
        Debug.WriteLine($"--> TabView_Loaded: Fired for tab '{vm.Header}'.");

        // --- Get Control References ---
        // The editor is available directly via its x:Name.
        _logOutputEditor = this.LogOutputEditor;

        // The OverviewRuler is inside the editor's template, so we must find it there.
        // First, ensure the template is applied so we can access its children.
        _logOutputEditor.ApplyTemplate();
        _overviewRuler = _logOutputEditor.Template.FindName("OverviewRuler", _logOutputEditor) as OverviewRulerMargin;

        if (_logOutputEditor is null || _overviewRuler is null)
        {
            Debug.WriteLine("!!! TabView_Loaded: Could not find LogOutputEditor or OverviewRuler. Aborting setup.");
            return;
        }

        Debug.WriteLine("TabView_Loaded: Successfully found LogOutputEditor and OverviewRuler.");
        RemoveAvalonCommandBindings();

        // --- Connect View and ViewModel ---
        _viewModel = vm;
        _viewModel.SetLogEditorInstance(_logOutputEditor);
        _viewModel.RequestScrollToEnd += ViewModel_RequestScrollToEnd;
        _viewModel.RequestScrollToLineIndex += ViewModel_RequestScrollToLineIndex;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;

        Debug.WriteLine($"--> TabView_Loaded: Called SetLogEditorInstance for tab '{vm.Header}'.");

        // --- Setup Editor Features ---
        TextView textView = _logOutputEditor.TextArea.TextView;
        if (textView == null)
            throw new InvalidOperationException("TextView not found within LogOutputEditor.");

        // Setup SelectedIndexHighlightTransformer
        _selectedIndexTransformer = new SelectedIndexHighlightTransformer();
        textView.SetResourceReference(TextView.TagProperty, "PersistedHighlightBrush");
        _selectedIndexTransformer.HighlightBrush = textView.Tag as Brush;
        _selectedIndexTransformer.FilteredLinesSource = _viewModel.FilteredLogLines;
        // The transformer is an IBackgroundRenderer, so it must be added to the BackgroundRenderers collection.
        textView.BackgroundRenderers.Add(_selectedIndexTransformer);

        // Setup ChunkSeparatorRenderer
        _chunkSeparator = new ChunkSeparatorRenderer(textView);
        textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush");
        Brush? separatorBrush = textView.ToolTip as Brush;
        _chunkSeparator.SeparatorBrush = separatorBrush ?? Brushes.Gray;
        textView.ClearValue(TextView.ToolTipProperty);
        textView.BackgroundRenderers.Add(_chunkSeparator);
        UpdateChunkSeparator(); // Initial update

        // --- Hook Editor UI Events ---
        _logOutputEditor.TextArea.PreviewMouseWheel += TextArea_PreviewMouseWheel;
        _logOutputEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _logOutputEditor.TextArea.SelectionChanged += LogOutputEditor_SelectionChanged;
        _logOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown;
        _logOutputEditor.TextArea.PreviewMouseDown += LogOutputEditor_PreviewMouseDown;
        _overviewRuler.PreviewMouseLeftButtonDown += OverviewRuler_PreviewMouseLeftButtonDown;
        _lastKnownCaretLine = _logOutputEditor.TextArea.Caret.Line;
    }

    private void TabView_Unloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null) return;

        switch (e.PropertyName)
        {
            case nameof(TabViewModel.HighlightedFilteredLineIndex):
                if (_selectedIndexTransformer != null)
                {
                    _selectedIndexTransformer.SelectedIndex = _viewModel.HighlightedFilteredLineIndex;
                    _logOutputEditor?.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
                }
                break;
            case nameof(TabViewModel.FilteredLogLines):
                UpdateChunkSeparator();
                break;
        }
    }

    private void UpdateChunkSeparator()
    {
        if (_chunkSeparator != null && _viewModel != null)
        {
            // TODO: Get ContextLines from a shared settings service or pass it down.
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, 0);
        }
    }

    #region Editor Interaction and Event Forwarding

    private void ViewModel_RequestScrollToLineIndex(object? sender, int lineIndex)
    {
        if (_logOutputEditor == null || lineIndex < 0 || lineIndex >= _logOutputEditor.LineCount)
        {
            return;
        }

        var line = _logOutputEditor.Document.GetLineByNumber(lineIndex + 1);
        _logOutputEditor.ScrollTo(line.LineNumber, 0);
        _logOutputEditor.TextArea.Caret.Position = new(line.LineNumber, 1);
    }

    private void ViewModel_RequestScrollToEnd(object? sender, EventArgs e)
    {
        if (_viewModel != null && _viewModel.IsAutoScrollEnabled)
        {
            _logOutputEditor?.ScrollToEnd();
        }
    }

    private void TextArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsAutoScrollEnabled)
        {
            _viewModel.IsAutoScrollEnabled = false;
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (_logOutputEditor == null || _viewModel == null) return;

        int newLine = _logOutputEditor.TextArea.Caret.Line;
        if (newLine != _lastKnownCaretLine)
        {
            _lastKnownCaretLine = newLine;
            int lineIndex = newLine - 1;
            if (lineIndex >= 0 && lineIndex < _viewModel.FilteredLogLines.Count)
            {
                _viewModel.HighlightedFilteredLineIndex = lineIndex;
            }
        }
    }

    private void LogOutputEditor_SelectionChanged(object? sender, EventArgs e)
    {
        if (_logOutputEditor == null || _viewModel == null)
            return;

        if (!_logOutputEditor.TextArea.Selection.IsEmpty)
        {
            // Update the ViewModel with the selected text
            _viewModel.SelectedText = _logOutputEditor.SelectedText;

            if (_viewModel.IsAutoScrollEnabled)
            {
                _viewModel.IsAutoScrollEnabled = false;
            }
        }
        else
        {
            // When selection is cleared, update the ViewModel
            _viewModel.SelectedText = null;
        }
    }

    private void LogOutputEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsAutoScrollEnabled && (e.Key == Key.PageUp || e.Key == Key.Up || e.Key == Key.PageDown || e.Key == Key.Down))
        {
            _viewModel.IsAutoScrollEnabled = false;
        }
    }

    private void LogOutputEditor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsAutoScrollEnabled)
        {
            _viewModel.IsAutoScrollEnabled = false;
        }
    }

    #endregion

    #region Overview Ruler Interaction

    private void OverviewRuler_RequestScrollOffset(object? sender, double verticalOffset)
    {
        _logOutputEditor?.ScrollToVerticalOffset(verticalOffset);
    }

    private void OverviewRuler_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel != null && _viewModel.IsAutoScrollEnabled)
        {
            _viewModel.IsAutoScrollEnabled = false;
        }
    }
    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        Loaded -= TabView_Loaded;
        Unloaded -= TabView_Unloaded;

        if (_overviewRuler != null)
        {
            _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
            _overviewRuler.PreviewMouseLeftButtonDown -= OverviewRuler_PreviewMouseLeftButtonDown;
        }

        if (_logOutputEditor?.TextArea != null)
        {
            _logOutputEditor.TextArea.PreviewMouseWheel -= TextArea_PreviewMouseWheel;
            _logOutputEditor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
            _logOutputEditor.TextArea.SelectionChanged -= LogOutputEditor_SelectionChanged;
            _logOutputEditor.TextArea.PreviewKeyDown -= LogOutputEditor_PreviewKeyDown;
            _logOutputEditor.TextArea.PreviewMouseDown -= LogOutputEditor_PreviewMouseDown;
        }

        if (_viewModel != null)
        {
            _viewModel.RequestScrollToEnd -= ViewModel_RequestScrollToEnd;
            _viewModel.RequestScrollToLineIndex -= ViewModel_RequestScrollToLineIndex;
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
