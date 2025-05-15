using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input; // Required for RoutedUICommand
using System.Windows.Documents; // Required for AdornerLayer
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;

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
    private EmptyDropTargetAdorner? _emptyDropAdorner;
    private AdornerLayer? _adornerLayer;

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
    private bool _disposed;

    private static Logonaut.Core.FileSystemSettingsService _settingsService = new();

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
            _adornerLayer = AdornerLayer.GetAdornerLayer(FilterTreeView); // Get layer once
            UpdateEmptyTreeViewAdornerVisibility(); // Initial check
        };

        // Set up initial window state
        Loaded += MainWindow_Loaded;

        SourceInitialized += MainWindow_SourceInitialized;

        // Add original line number and separator margins (code-behind approach)
        SetupCustomMargins();

        _logOutputEditor.Loaded += LogOutputEditor_Loaded; // Hook up event handlers AFTER the template is applied
        _logOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown; // Enable clipboard paste functionality. Only preview events seem to work here.
        _logOutputEditor.TextArea.PreviewMouseDown += LogOutputEditor_PreviewMouseDown; // Handle mouse clicks for search reference point. Only preview events seem to work here.
        _viewModel.ActiveTreeRootNodes.CollectionChanged += ActiveTreeRootNodes_CollectionChanged;

        Closing += MainWindow_Closing;
    }

    private void ActiveTreeRootNodes_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyTreeViewAdornerVisibility();
    }

    // Helper method to manage overall visibility and resting state
    private void UpdateEmptyTreeViewAdornerVisibility()
    {
        if (_adornerLayer == null) _adornerLayer = AdornerLayer.GetAdornerLayer(FilterTreeView);
        if (_adornerLayer == null) return;

        bool isTreeEmpty = (_viewModel.ActiveFilterProfile?.RootFilterViewModel == null && _viewModel.ActiveTreeRootNodes.Count == 0);

        if (isTreeEmpty)
        {
            if (_emptyDropAdorner == null)
            {
                _emptyDropAdorner = new EmptyDropTargetAdorner(FilterTreeView);
                _adornerLayer.Add(_emptyDropAdorner);
            }
            _emptyDropAdorner.SetVisualState(false); // Set to resting state
            _emptyDropAdorner.Visibility = Visibility.Visible;
        }
        else
        {
            if (_emptyDropAdorner != null)
            {
                _emptyDropAdorner.Visibility = Visibility.Collapsed;
                // Optionally remove and nullify if you prefer to recreate it always when tree becomes empty again
                // _adornerLayer.Remove(_emptyDropAdorner);
                // _emptyDropAdorner = null;
            }
        }
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
        _selectedIndexTransformer.FilteredLinesSource = _viewModel.FilteredLogLines; // Initial set
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

        _logOutputEditor.TextArea.SelectionChanged += LogOutputEditor_SelectionChanged;
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
    // TOOD: This is unused? That is maybe a bug.
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
