using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data; // Required for Binding
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input; // Required for RoutedUICommand
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI
{
    // TODO: Keep track of optional selected line in log window. Use highlighting to show.
    public partial class MainWindow : Window, IDisposable
    {
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
        private static Logonaut.LogTailing.LogTailerService _logTailerService = new();

        // It seems as if an constructor without parameters is required for XAML to work properly.
        // Inject a settings service.
        public MainWindow() : this(new MainViewModel(_settingsService, _logTailerService)) {}

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

            // Apply dark title bar if supported
            if (IsWindows10OrGreater())
            {
                EnableDarkTitleBar();
            }

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

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // --- Overview Ruler Setup ---
            _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(_logOutputEditor);
            if (_overviewRuler is null)
                throw new InvalidOperationException("OverviewRulerMargin not found in TextEditor template.");
            _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
            // No need for the extra Unloaded lambda here, LogOutputEditor_Unloaded handles it.

            // Get TextView *once*
            TextView textView = _logOutputEditor.TextArea.TextView;
            if (textView == null) // Add null check for safety
                 throw new InvalidOperationException("TextView not found within LogOutputEditor.");

            _selectedIndexTransformer = new SelectedIndexHighlightTransformer();
            // Get the initial brush from the resource dictionary via the Tag proxy
            textView.SetResourceReference(TextView.TagProperty, "PersistedHighlightBrush");
            _selectedIndexTransformer.HighlightBrush = textView.Tag as Brush;
            // Add transformer to the text view
            textView.LineTransformers.Add(_selectedIndexTransformer);

            // --- Chunk Separator Setup ---
            _chunkSeparator = new ChunkSeparatorRenderer(textView);

            // Chunk Separator Brush Binding (using TextView.ToolTip as proxy)
            textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush"); // Use ToolTipProperty

            var chunkBrushBinding = new Binding("ToolTip")
            {
                Source = textView,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(
                _chunkSeparator,
                ChunkSeparatorRenderer.SeparatorBrushProperty,
                chunkBrushBinding // Use the correct binding variable
            );
            textView.BackgroundRenderers.Add(_chunkSeparator);
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

            // --- Final Cleanup Subscription ---
            _logOutputEditor.Unloaded += LogOutputEditor_Unloaded; // Subscribe the main unload handler ONCE
        }

        private void LogOutputEditor_Unloaded(object? sender, RoutedEventArgs? e)
        {
            // Clean up Overview Ruler binding
            if (_overviewRuler != null)
                _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
            _overviewRuler = null; // Release reference

            TextView textView = _logOutputEditor.TextArea.TextView;
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
    }
}