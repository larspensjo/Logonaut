using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data; // Required for Binding
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input; // Required for RoutedUICommand
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Editing; // Required for Caret
using ICSharpCode.AvalonEdit.Document; // Required for DocumentLine
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using System.Diagnostics;

namespace Logonaut.UI;

/*
 * Implements the code-behind for Logonaut's main application window.
 *
 * Purpose:
 * Defines the primary user interface structure (from MainWindow.xaml) and handles
 * direct UI interactions, event wiring, and view-specific logic that complements
 * the MainViewModel.
 *
 * Role & Responsibilities:
 * - Hosts the UI elements defined in XAML.
 * - Sets the DataContext to an instance of MainViewModel, enabling data binding.
 * - Handles window lifecycle events (Loaded, Closing, SourceInitialized) for setup and cleanup.
 * - Manages UI-specific components like AvalonEdit custom margins (line numbers, separators),
 *   the overview ruler, and highlighting transformers.
 * - Responds to events from the ViewModel (e.g., RequestScrollToLineIndex, RequestScrollToEnd)
 *   to manipulate UI controls directly, often for performance or because the action is
 *   inherently view-related (scrolling, focus).
 * - Handles some direct user input events (e.g., PreviewMouseDown on editor for selection,
 *   PreviewKeyDown for paste/search, input event filtering for auto-scroll disabling)
 *   that are more easily managed in code-behind.
 * - Wires up UI command bindings (e.g., for focusing specific text boxes).
 * - Contains necessary platform interop code (e.g., for dark title bar).
 *
 * Deviations:
 * Contains more code-behind than a strict MVVM pattern might suggest, primarily for
 * performance optimizations with AvalonEdit, complex UI interactions involving custom
 * renderers/margins, and focus management.
 *
 * Manages its UI-related resources and subscriptions, coordinating cleanup via IDisposable
 * and the Closing event.
 */
public partial class MainWindow : Window, IDisposable
{
    private int _lastKnownCaretLine = -1; // Field to track the last caret line

    public static readonly RoutedUICommand ToggleSimulatorConfigCommand = new RoutedUICommand(
        "Toggle Simulator Configuration Panel", "ToggleSimulatorConfigCommand", typeof(MainWindow)
    );

    private void ToggleSimulatorConfig_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // Can always execute if the ViewModel exists
        e.CanExecute = DataContext is MainViewModel;
    }

    private void ToggleSimulatorConfig_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // Toggle the boolean property on the ViewModel
            viewModel.IsSimulatorConfigurationVisible = !viewModel.IsSimulatorConfigurationVisible;
        }
    }

    // Define a command for focusing the Go To Line box
    public static readonly RoutedUICommand GoToLineBoxFocusCommand = new RoutedUICommand(
        "Focus Go To Line Box", "GoToLineBoxFocusCommand", typeof(MainWindow)
    );

    // --- Command Handlers for Go To Line Focus ---
    private void GoToLineBoxFocus_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // We can always try to focus the box if it exists
        e.CanExecute = true;
    }

    private void GoToLineBoxFocus_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // Access the TextBox using its x:Name and set focus
        JumpToLineTextBox.Focus();
        JumpToLineTextBox.SelectAll(); // Select existing text for easy replacement
    }

    // Define a command for focusing the Search box
    public static readonly RoutedUICommand FocusSearchCommand = new RoutedUICommand(
        "Focus Search Box", "FocusSearchCommand", typeof(MainWindow)
    );

    // Command Handlers for Search Focus <<<
    private void FocusSearch_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // Can always execute if the search box exists (which it should)
        e.CanExecute = true;
    }

    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll(); // Select existing text
    }

    // --- Dark Title Bar Support ---
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static bool IsWindows10OrGreater(int build = -1)
    {
        try
        {
            return Environment.OSVersion.Version.Major >= 10 && 
                    (build == -1 || Environment.OSVersion.Version.Build >= build);
        }
        catch
        {
            return false; // Avoid potential exceptions
        }
    }

    private readonly MainViewModel _viewModel;
    private readonly TextEditor _logOutputEditor; // Just for convenience. Referencing LogOutputEditor triggers an Intellisense error.
    private OverviewRulerMargin? _overviewRuler;
    private ChunkSeparatorRenderer? _chunkSeparator;
    private bool _disposed;
    private SelectedIndexHighlightTransformer? _selectedIndexTransformer;

    private static Logonaut.Core.FileSystemSettingsService _settingsService = new();

    // Field for DnD visual feedback +++
    private TreeViewItem? _dragOverTreeViewItem = null;
    private Brush? _originalDragOverItemBrush = null;
    private const string DragDropDataFormatFilterType = "LogonautFilterTypeIdentifier"; // Custom data format key

    // Enable injection of the ViewModel for testing purposes
    public MainWindow(MainViewModel viewModel)
    {
        // InitializeComponent() is the method generated from the XAML. When it runs, it parses the XAML, creates the UI elements, and wires them up.
        InitializeComponent();
        _logOutputEditor = LogOutputEditor; // This will trigger an Intellisense error
        DataContext = viewModel;
        _viewModel = viewModel;
        
        _viewModel.PropertyChanged += ViewModel_PropertyChanged; // Subscribe to model updates to update chunk separators
        _viewModel.RequestScrollToEnd += ViewModel_RequestScrollToEnd;
        _viewModel.RequestScrollToLineIndex += ViewModel_RequestScrollToLineIndex;

        // Apply dark title bar if supported
        if (IsWindows10OrGreater())
        {
            EnableDarkTitleBar();
        }

        // --- Pass editor instance to ViewModel AFTER it's loaded ---
        _logOutputEditor.Loaded += (s, e) =>
        {
            _viewModel.SetLogEditorInstance(_logOutputEditor);
            // Any other logic previously in LogOutputEditor_Loaded that depends
            // on the ViewModel knowing about the editor can go here or be
            // triggered by SetLogEditorInstance if needed.
        };

        // Set up initial window state
        Loaded += MainWindow_Loaded;

        SourceInitialized += MainWindow_SourceInitialized;

        // Add original line number and separator margins (code-behind approach)
        SetupCustomMargins();

        // Hook up event handlers AFTER the template is applied
        _logOutputEditor.Loaded += LogOutputEditor_Loaded;
        
        // Enable clipboard paste functionality. Only preview events seem to work here.
        _logOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown;

        // Handle mouse clicks for search reference point. Only preview events seem to work here.
        _logOutputEditor.TextArea.PreviewMouseDown += LogOutputEditor_PreviewMouseDown;

        Closing += MainWindow_Closing;
    }

    private void ViewModel_RequestScrollToLineIndex(object? sender, int lineIndex)
    {
        // Ensure this runs on the UI thread if there's any doubt
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollToSelectedLine(lineIndex);
        }));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Ensure editor unload cleanup runs if window closes before unload fires
            LogOutputEditor_Unloaded(null, null);

            if (_viewModel != null) {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.RequestScrollToEnd -= ViewModel_RequestScrollToEnd;
                _viewModel.RequestScrollToLineIndex -= ViewModel_RequestScrollToLineIndex;
                // Dispose ViewModel
                _viewModel.Cleanup(); // Use existing cleanup which includes Dispose
            }

            _disposed = true;
            GC.SuppressFinalize(this); // If you add a finalizer
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_chunkSeparator != null && (e.PropertyName == nameof(MainViewModel.FilteredLogLines) || e.PropertyName == nameof(MainViewModel.ContextLines)))
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

        if (_selectedIndexTransformer != null && _logOutputEditor.TextArea?.TextView != null)
        {
            if (e.PropertyName == nameof(MainViewModel.HighlightedFilteredLineIndex))
            {
                int newLineNumberInFilteredDoc = _viewModel.HighlightedFilteredLineIndex >= 0
                                    ? _viewModel.HighlightedFilteredLineIndex + 1 // Convert 0-based index to 1-based line number
                                    : -1; // Disable if index is -1

                // Get the brush from the TextView's Tag property (where we stored the resource)
                var highlightBrush = _logOutputEditor.TextArea.TextView.Tag as Brush;

                // Update the transformer state (this method handles redraw)
                _selectedIndexTransformer.UpdateState(newLineNumberInFilteredDoc, highlightBrush, _logOutputEditor.TextArea.TextView);

                // Disable AutoScroll whenever a line is selected (by either method)
                if (_viewModel.HighlightedFilteredLineIndex >= 0 && _viewModel.IsAutoScrollEnabled)
                {
                    _viewModel.IsAutoScrollEnabled = false;
                }
            }
        }

        // Clear Status Message on Successful Jump ---
        // Optional: Could also be done via a timer in the ViewModel
        if (e.PropertyName == nameof(MainViewModel.HighlightedOriginalLineNumber) &&
            !string.IsNullOrEmpty(_viewModel.JumpStatusMessage) &&
            _viewModel.HighlightedOriginalLineNumber.ToString() == _viewModel.TargetOriginalLineNumberInput)
        {
            _viewModel.JumpStatusMessage = string.Empty; // Clear message if jump succeeded
        }
    }

    private void ViewModel_RequestScrollToEnd(object? sender, EventArgs e)
    {
        // Ensure this runs on the UI thread if there's any doubt,
        // but it should be called from the VM's Post callback.
        // Dispatcher.BeginInvoke(new Action(() =>
        // {
                _logOutputEditor?.ScrollToEnd();
        // }), DispatcherPriority.Background); // Background is usually safe
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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Overview Ruler Setup ---
        _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(_logOutputEditor);
        if (_overviewRuler is null)
            throw new InvalidOperationException("OverviewRulerMargin not found in TextEditor template.");
        _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
        // Handle user clicking the ruler
        _overviewRuler.PreviewMouseLeftButtonDown += OverviewRuler_PreviewMouseLeftButtonDown;

        // Get TextView *once*
        TextView textView = _logOutputEditor.TextArea.TextView;
        if (textView == null)
                throw new InvalidOperationException("TextView not found within LogOutputEditor.");

        _selectedIndexTransformer = new SelectedIndexHighlightTransformer();
        // Get the initial brush from the resource dictionary via the Tag proxy
        textView.SetResourceReference(TextView.TagProperty, "PersistedHighlightBrush");
        _selectedIndexTransformer.HighlightBrush = textView.Tag as Brush;
        // Add transformer to the text view
        textView.LineTransformers.Add(_selectedIndexTransformer);

        // --- Chunk Separator Setup ---
        _chunkSeparator = new ChunkSeparatorRenderer(textView);

        // 1. Temporarily use ToolTip to resolve the resource against the TextView's context
        textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush");

        // 2. Get the resolved brush directly from the ToolTip property NOW
        Brush? separatorBrush = textView.ToolTip as Brush;

        // 3. Set the brush directly on the renderer's DP.
        //    The renderer's OnSeparatorBrushChanged callback will handle creating the pen.
        //    Provide a fallback default brush if the resource wasn't found or is somehow null.
        _chunkSeparator.SeparatorBrush = separatorBrush ?? Brushes.Gray; // Use fallback Gray if needed

        // 4. Clear the ToolTip property on the TextView *immediately* after getting the value.
        //    This removes the unwanted tooltip from the editor.
        textView.ClearValue(TextView.ToolTipProperty);

        // 5. Add the renderer to the TextView.
        //    NO BINDING IS NEEDED HERE ANYMORE.
        textView.BackgroundRenderers.Add(_chunkSeparator);

        // 6. Perform the initial update based on current ViewModel state.
        _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

        // --- Final Cleanup Subscription ---
        _logOutputEditor.Unloaded += LogOutputEditor_Unloaded; // Subscribe the main unload handler ONCE

        // --- Subscribe to User Input Events for Scrolling ---
        _logOutputEditor.TextArea.PreviewMouseWheel += TextArea_PreviewMouseWheel;
        _logOutputEditor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;

        // --- Subscribe to Caret Position Changes ---
        _logOutputEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _lastKnownCaretLine = _logOutputEditor.TextArea.Caret.Line; // Initialize
    }

    private void LogOutputEditor_Unloaded(object? sender, RoutedEventArgs? e)
    {
        // Clean up Overview Ruler binding
        if (_overviewRuler != null) {
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
        if (textView is null)
            throw new InvalidOperationException("TextView not found within LogOutputEditor.");

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
        if (_chunkSeparator != null && textView is not null)
        {
            // Clear bindings
            BindingOperations.ClearBinding(_chunkSeparator, ChunkSeparatorRenderer.SeparatorBrushProperty);
            textView.ClearValue(TextView.TagProperty); // Clear the resource reference on Tag

            // Remove renderer and dispose
            if(textView.BackgroundRenderers.Contains(_chunkSeparator))
            {
            textView.BackgroundRenderers.Remove(_chunkSeparator);
            }
            _chunkSeparator.Dispose();
        }
        _chunkSeparator = null; // Release reference
    }

   private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (sender is Caret caret && DataContext is MainViewModel viewModel)
        {
            int currentCaretLine = caret.Line; // 1-based line number

            // Check if the line actually changed
            if (currentCaretLine != _lastKnownCaretLine)
            {
                // Update the last known line
                _lastKnownCaretLine = currentCaretLine;

                // Calculate the 0-based index for the ViewModel
                int filteredLineIndex = currentCaretLine - 1;

                // Update the ViewModel's highlighted line index
                // Ensure the index is valid before setting
                if (filteredLineIndex >= 0 && filteredLineIndex < viewModel.FilteredLogLines.Count)
                {
                    viewModel.HighlightedFilteredLineIndex = filteredLineIndex;

                    // IMPORTANT: Disable AutoScroll whenever the user manually changes the line
                    if (viewModel.IsAutoScrollEnabled)
                    {
                        viewModel.IsAutoScrollEnabled = false;
                        // Debug.WriteLine("AutoScroll disabled due to Caret Line Change.");
                    }
                }
                else
                {
                    // If the line number is somehow invalid (e.g., caret moved beyond document end temporarily?)
                    // you might choose to clear the highlight or ignore. Let's clear it for safety.
                    if (viewModel.HighlightedFilteredLineIndex != -1)
                    {
                        viewModel.HighlightedFilteredLineIndex = -1;
                    }
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

    private void ProfileNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            // commit on Enter
            var tb = (TextBox)sender;
            if (tb.DataContext is FilterProfileViewModel vm)
                vm.EndRenameCommand.Execute(null);
            // optionally move focus so LostFocus also fires
            System.Windows.Input.Keyboard.ClearFocus();
        }
    }

    private void ProfileNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb
        && tb.DataContext is FilterProfileViewModel vm)
        {
            // commit the rename
            vm.EndRenameCommand.Execute(null);
        }
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
            _logOutputEditor.Unloaded += (s, ev) => {
                if (_overviewRuler != null)
                {
                    _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
                }
                // Also unsubscribe from Loaded/Unloaded? Might not be necessary if window closes.
            };
    }

    private void LogOutputEditor_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left && DataContext is ViewModels.MainViewModel viewModel)
        {
            var textView = _logOutputEditor.TextArea.TextView;
            if (!textView.IsLoaded || !textView.VisualLinesValid)
                throw new InvalidOperationException("TextView is not loaded or visual lines are not valid.");

            // 1. Get the click position relative to the TextView control
            Point pointRelativeToTextView = e.GetPosition(textView);

            // 2. Calculate the click position relative to the start of the document (add scroll offsets)
            Point pointInDocument = new Point(
                pointRelativeToTextView.X + textView.ScrollOffset.X,
                pointRelativeToTextView.Y + textView.ScrollOffset.Y
            );

            // 3. Use the document-relative point to get the position
            TextViewPosition? positionInfo = textView.GetPosition(pointInDocument); // Pass the corrected point

            int clickedFilteredLineIndex = -1; // Default to -1 if no line is clicked
            if (positionInfo.HasValue)
            {
                // 4. Get the VisualLine object associated with the position
                //    We can use the Line number from the positionInfo to get the DocumentLine,
                //    and then the VisualLine. Or, less reliably, try GetVisualLineFromVisualTop again.
                VisualLine? clickedVisualLine = textView.GetVisualLine(positionInfo.Value.Line);

                if (clickedVisualLine != null)
                {
                    int clickedDocumentLineNumber = clickedVisualLine.FirstDocumentLine.LineNumber;
                    clickedFilteredLineIndex = clickedDocumentLineNumber - 1;
                }
            }
            viewModel.HighlightedFilteredLineIndex = clickedFilteredLineIndex;
        }
    }

    // Handler for the ruler's request to scroll
    private void OverviewRuler_RequestScrollOffset(object? sender, double requestedOffset)
    {
        _logOutputEditor.ScrollToVerticalOffset(requestedOffset);
    }


    // Helper to find a child element of a specific type in the visual tree
    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child != null && child is T typedChild)
            {
                return typedChild;
            }
            else
            {
                if (child is null)
                    throw new ArgumentNullException(nameof(child), "Child is null. This should not happen.");
                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
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


    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // The window handle is now available
        if (IsWindows10OrGreater())
        {
            EnableDarkTitleBar();
        }
    }

    private void EnableDarkTitleBar()
    {
        var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
            return;

        int useImmersiveDarkMode = 1;
        if (IsWindows10OrGreater(20180)) // Windows 10 20H1 or later
        {
            DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        }
        else
        {
            DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
        }
    }

    // Update SelectedFilter in ViewModel when TreeView selection changes
    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            // The TreeView's SelectedItem is the data item (FilterViewModel)
            viewModel.SelectedFilterNode = e.NewValue as FilterViewModel;
        }
    }

    // Call ViewModel cleanup method when the window is closing
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Cleanup();
        }
    }

    // Simple Exit menu item handler
    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        this.Close(); // Trigger the Window_Closing event
    }

    #region Drag-and-Drop Handlers for Filter Palette and TreeView

    // --- Drag Source: Filter Palette Item ---
    // Initiates a drag operation when a filter type is dragged from the palette.
    private void PaletteItemsControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? originalSource = e.OriginalSource as DependencyObject;
        ContentPresenter? paletteItemContainer = null;

        while (originalSource != null && originalSource != sender as ItemsControl)
        {
            if (originalSource is ContentPresenter cp && cp.DataContext is Logonaut.Common.FilterTypeDescriptor)
            {
                paletteItemContainer = cp;
                break;
            }
            originalSource = VisualTreeHelper.GetParent(originalSource);
        }

        if (paletteItemContainer != null && paletteItemContainer.DataContext is Logonaut.Common.FilterTypeDescriptor descriptor)
        {
            DataObject dragData = new DataObject(DragDropDataFormatFilterType, descriptor.TypeIdentifier);
            DragDrop.DoDragDrop(paletteItemContainer, dragData, DragDropEffects.Copy);
            e.Handled = true;
        }
    }

    // --- Drop Target: FilterTreeView ---
    private void FilterTreeView_DragEnter(object sender, DragEventArgs e)
    {
        UpdateDragDropEffects(e);
    }

    private void FilterTreeView_DragOver(object sender, DragEventArgs e)
    {
        UpdateDragDropEffects(e);
    }

    // Determines the effect of a drag operation over the TreeView and provides visual feedback.
    private void UpdateDragDropEffects(DragEventArgs e)
    {
        e.Effects = DragDropEffects.None; // Default to no drop
        ClearDropTargetAdornment();       // Clear previous highlight

        if (e.Data.GetDataPresent(DragDropDataFormatFilterType))
        {
            Point pt = e.GetPosition(FilterTreeView);
            // Hit test for TreeViewItem directly under mouse
            TreeViewItem? targetTVI = GetVisualAncestor<TreeViewItem>(FilterTreeView.InputHitTest(pt) as DependencyObject);
            FilterViewModel? targetVM = (targetTVI?.DataContext) as FilterViewModel;

            var mainViewModel = DataContext as MainViewModel;
            if (mainViewModel == null || mainViewModel.ActiveFilterProfile == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            // Check if dropping onto a valid composite item
            if (targetVM != null && targetVM.Filter is CompositeFilter)
            {
                e.Effects = DragDropEffects.Copy; // Allow copy (add)
                ApplyDropTargetAdornment(targetTVI);
            }
            // Allow drop onto empty TreeView space if:
            // 1. The active profile's root is null (tree is completely empty)
            // OR 2. The active profile's root is a composite filter (can add to root)
            else if (targetVM == null && 
                     (mainViewModel.ActiveFilterProfile.RootFilterViewModel == null ||
                      mainViewModel.ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter))
            {
                e.Effects = DragDropEffects.Copy; // Allow copy (add to root or as new root)
                // No specific TreeViewItem to highlight for empty space drop
            }
        }
        e.Handled = true;
    }
    
    private void FilterTreeView_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropTargetAdornment();
        e.Handled = true;
    }

    // Handles the drop action on the FilterTreeView.
    private void FilterTreeView_Drop(object sender, DragEventArgs e)
    {
        ClearDropTargetAdornment(); // Clear highlight after drop
        var mainViewModel = DataContext as MainViewModel;
        if (mainViewModel == null) return;

        string? filterTypeIdentifier = e.Data.GetData(DragDropDataFormatFilterType) as string;
        if (string.IsNullOrEmpty(filterTypeIdentifier)) return;

        FilterViewModel? targetParentVM = null;
        int? dropIndex = null; // For future precise insertion; null means append for now.

        Point pt = e.GetPosition(FilterTreeView);
        TreeViewItem? targetTVI = GetVisualAncestor<TreeViewItem>(FilterTreeView.InputHitTest(pt) as DependencyObject);
        FilterViewModel? hitTestVM = (targetTVI?.DataContext) as FilterViewModel;

        if (hitTestVM != null) // Dropped on an existing item
        {
            if (hitTestVM.Filter is CompositeFilter)
            {
                targetParentVM = hitTestVM;
                // For Step 1, always append. Index calculation for specific position comes later.
                dropIndex = targetParentVM.Children.Count; 
            }
            else
            {
                // Dropping on a non-composite leaf is handled by DragOver setting Effects to None.
                // If it still reaches here, do nothing.
                e.Effects = DragDropEffects.None;
                return;
            }
        }
        // If hitTestVM is null, it implies a drop on empty TreeView space.
        // mainViewModel.ExecuteAddFilterFromDrop will handle logic for adding to root or empty tree.
        // In this case, targetParentVM remains null, and ExecuteAddFilterFromDrop will figure it out.

        mainViewModel.ExecuteAddFilterFromDrop(filterTypeIdentifier, targetParentVM, dropIndex);
        e.Handled = true;
    }

    // --- Visual Feedback Helpers ---

    // Applies a temporary background highlight to a TreeViewItem during drag-over.
    private void ApplyDropTargetAdornment(TreeViewItem? tvi)
    {
        if (tvi != null)
        {
            _dragOverTreeViewItem = tvi;
            _originalDragOverItemBrush = tvi.Background;
            // Use a theme-aware highlight brush if possible, or fallback
            var highlightBrush = TryFindResource("AccentBrush") as Brush ?? SystemColors.HighlightBrush; // Using AccentBrush for highlight
            tvi.Background = highlightBrush;
        }
    }

    // Clears any temporary background highlight from a TreeViewItem.
    private void ClearDropTargetAdornment()
    {
        if (_dragOverTreeViewItem != null && _originalDragOverItemBrush != null)
        {
            _dragOverTreeViewItem.Background = _originalDragOverItemBrush;
        }
        _dragOverTreeViewItem = null;
        _originalDragOverItemBrush = null;
    }
    
    // --- Helper Methods ---

    // Gets the FilterViewModel associated with a DependencyObject (typically a UI element within a TreeViewItem).
    private FilterViewModel? GetFilterViewModelFromElement(DependencyObject? element)
    {
        // Traverse up to find TreeViewItem and get its DataContext
        TreeViewItem? tvi = GetVisualAncestor<TreeViewItem>(element);
        return tvi?.DataContext as FilterViewModel;
    }

    // Finds the first visual ancestor of a specific type.
    public static T? GetVisualAncestor<T>(DependencyObject? d) where T : class
    {
        while (d != null)
        {
            if (d is T tItem) return tItem;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    #endregion
}
